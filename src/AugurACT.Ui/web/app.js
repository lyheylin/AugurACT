'use strict';

// Encounters come from the plugin (ACT's encounter list, analyzed on request) or, in the page the CLI writes,
// from an inline "barrage-report". Both end up as the same encounter objects here.
//
// Log text holds player names and chat, so everything from it is written with textContent.

const host = window.chrome?.webview;
const encounters = new Map(); // id -> encounter
const rows = document.querySelector('#encounters tbody');
let expanded = null; // { encounter, row, detailRow, timeline }
const panelOpen = new Map(); // panel name -> open; kept while switching encounters
let encounterData = null;
let pendingUpdate = null; // encounter the update dialog is open for

const $ = (id) => document.getElementById(id);

// ---- formatting ----

function when(epoch) {
  const d = new Date(epoch * 1000);
  return `${d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} ${d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}`;
}

function time(epoch) {
  return new Date(epoch * 1000).toLocaleTimeString(undefined, { hour12: false });
}

function duration(seconds) {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

function damage(value) {
  for (const [scale, suffix] of [[1e15, 'Q'], [1e12, 'T'], [1e9, 'B'], [1e6, 'M'], [1e3, 'K']]) {
    if (value >= scale) return `${(value / scale).toFixed(1)}${suffix}`;
  }
  return String(Math.round(value));
}

function fmtSeconds(value) {
  return value == null ? '-' : `${Math.round(value * 10) / 10} s`;
}

function median(values) {
  const sorted = values.filter((v) => v != null).sort((a, b) => a - b);
  if (!sorted.length) return null;
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

// Seconds between casts of one NPC instance; longer gaps are separate pulls.
function intervals(casts) {
  const byId = new Map();
  for (const c of casts) {
    if (!byId.has(c.npcId)) byId.set(c.npcId, []);
    byId.get(c.npcId).push(c.readiedAt);
  }
  const out = [];
  for (const times of byId.values()) {
    times.sort((a, b) => a - b);
    for (let i = 1; i < times.length; i++) {
      const gap = times[i] - times[i - 1];
      if (gap <= 300) out.push(gap);
    }
  }
  return out;
}

function count(casts, outcome) {
  return casts.filter((c) => c.outcome === outcome).length;
}

function make(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
}

function notice(text, isError) {
  const box = $('notice');
  box.textContent = text;
  box.classList.toggle('error', !!isError);
  box.hidden = !text;
}

function send(message) {
  host?.postMessage(message);
}

// ---- encounter list ----

const COLUMNS = 9;

// Timeline origin and end: ACT's start and end, widened to any barrage warned before or released after them.
function span(encounter) {
  const casts = encounter.barrages || [];
  let start = encounter.start || encounter.summaryStart || 0;
  let end = encounter.end || encounter.summaryEnd || start;
  for (const c of casts) {
    if (!start || c.readiedAt < start) start = c.readiedAt;
    end = Math.max(end, c.resolvedAt ?? c.readiedAt);
  }
  return { start, end };
}

function upsert(info, prepend) {
  const existing = encounters.get(info.id);
  if (existing) {
    Object.assign(existing, info);
    fillRow(existing);
    return existing;
  }

  const encounter = { ...info, barrages: info.barrages ?? null, state: info.barrages ? 'done' : 'pending' };
  encounters.set(encounter.id, encounter);

  const row = document.createElement('tr');
  row.className = 'encounter';
  row.tabIndex = 0;
  row.setAttribute('aria-expanded', 'false');
  for (let i = 0; i < COLUMNS; i++) row.appendChild(document.createElement('td'));
  row.cells[3].className = 'num';
  for (const i of [4, 5, 7]) row.cells[i].className = 'num';
  row.cells[6].className = 'num missed-count';
  row.cells[8].className = 'host-only action';
  row.cells[8].hidden = !host;

  if (host) {
    const button = make('button', 'update-data', 'Update encounters data');
    button.type = 'button';
    button.title = 'Record this fight in the encounters data, choosing its main NPC';
    button.addEventListener('click', (event) => {
      event.stopPropagation();
      openUpdate(encounter);
    });
    row.cells[8].appendChild(button);
  }

  row.addEventListener('click', () => toggle(encounter));
  row.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      toggle(encounter);
    }
  });

  encounter.row = row;
  if (prepend) rows.prepend(row);
  else rows.appendChild(row);
  fillRow(encounter);

  $('encounters').hidden = false;
  $('empty').hidden = true;
  return encounter;
}

function fillRow(encounter) {
  const { row } = encounter;
  const { start, end } = span(encounter);
  const casts = encounter.barrages;
  const pending = encounter.state === 'pending';
  const numbers = casts
    ? [casts.length, count(casts, 'Blocked'), count(casts, 'Missed'), count(casts, 'Unresolved')]
    : [null, null, null, null];

  const cells = [
    encounter.title,
    encounter.zone || '',
    start ? when(start) : '',
    start && end > start ? duration(end - start) : '',
    ...numbers.map((n) => (n == null ? (pending ? '…' : encounter.state === 'error' ? '?' : '') : n || '')),
  ];
  cells.forEach((text, i) => { row.cells[i].textContent = text; });
  row.cells[0].title = encounter.title;
  row.cells[1].title = encounter.zone || '';
  row.classList.toggle('no-barrage', !!casts && casts.length === 0);
  row.classList.toggle('pending', pending);
  row.title = encounter.state === 'error' ? `${encounter.error} Click to try again.` : encounter.source || '';
}

function toggle(encounter) {
  if (expanded?.encounter === encounter) {
    collapse();
    return;
  }
  collapse();

  const detailRow = document.createElement('tr');
  detailRow.className = 'detail-row';
  const cell = detailRow.insertCell();
  cell.colSpan = host ? COLUMNS : COLUMNS - 1;
  cell.appendChild($('detail-template').content.cloneNode(true));
  encounter.row.after(detailRow);
  encounter.row.classList.add('expanded');
  encounter.row.setAttribute('aria-expanded', 'true');

  const detail = cell.querySelector('.enc-detail');
  detail.querySelector('.reset-zoom').addEventListener('click', () => expanded?.timeline?.reset());
  for (const panel of detail.querySelectorAll('.side-panel')) {
    const name = panel.dataset.panel;
    panel.querySelector('.side-toggle').addEventListener('click', () => {
      panelOpen.set(name, !(panelOpen.get(name) ?? true));
      applyPanel(panel);
    });
    applyPanel(panel);
  }

  expanded = { encounter, row: encounter.row, detailRow, detail, timeline: null };
  renderDetail();

  // Background analysis may not have reached this one yet, or it failed; ask for it now.
  if (encounter.state !== 'done') {
    encounter.state = 'pending';
    renderDetail();
    send({ type: 'analyze', id: encounter.id });
  }
}

function collapse() {
  if (!expanded) return;
  expanded.timeline?.dispose();
  expanded.detailRow.remove();
  expanded.row.classList.remove('expanded');
  expanded.row.setAttribute('aria-expanded', 'false');
  expanded = null;
}

function applyPanel(panel) {
  const open = panelOpen.get(panel.dataset.panel) ?? true;
  panel.classList.toggle('closed', !open);
  panel.querySelector('.side-toggle').setAttribute('aria-expanded', String(open));
}

function renderDetail() {
  if (!expanded) return;
  const { encounter, detail } = expanded;
  const message = detail.querySelector('.detail-message');
  const timelineHost = detail.querySelector('.timeline');
  const casts = encounter.barrages;

  expanded.timeline?.dispose();
  expanded.timeline = null;
  detail.querySelector('.cast-detail').hidden = true;
  renderSide(detail, casts);

  if (encounter.state === 'error') {
    message.textContent = encounter.error;
    message.hidden = false;
    timelineHost.hidden = true;
    return;
  }
  if (!casts) {
    message.textContent = 'Analyzing this encounter…';
    message.hidden = false;
    timelineHost.hidden = true;
    return;
  }
  if (!casts.length) {
    message.textContent = 'No barrages in this encounter.';
    message.hidden = false;
    timelineHost.hidden = true;
    return;
  }

  message.hidden = true;
  timelineHost.hidden = false;
  const { start, end } = span(encounter);
  const fight = { title: encounter.title, start, end, barrages: casts };
  expanded.timeline = window.AugurTimeline.create(timelineHost, fight, (cast) => showCast(detail, cast, fight));
}

function renderSide(detail, casts) {
  const counts = detail.querySelector('.counts');
  counts.replaceChildren();
  const list = casts || [];
  const facts = [
    ['Barrages', list.length, ''],
    ['Blocked', count(list, 'Blocked'), 'blocked'],
    ['Missed', count(list, 'Missed'), 'missed'],
    ['No release', count(list, 'Unresolved'), 'unresolved'],
  ];
  const cancelled = count(list, 'Cancelled');
  if (cancelled) facts.push(['Cancelled', cancelled, 'cancelled']);
  for (const [label, value, cls] of facts) {
    const dt = make('dt', null, label);
    if (cls) dt.prepend(make('i', `sw ${cls}`));
    counts.append(dt, make('dd', null, casts ? String(value) : '…'));
  }

  const stats = detail.querySelector('.npc-stats');
  stats.replaceChildren();
  const byNpc = new Map();
  for (const c of list) {
    if (!byNpc.has(c.npc)) byNpc.set(c.npc, []);
    byNpc.get(c.npc).push(c);
  }
  for (const [npc, own] of byNpc) {
    const released = own.filter((c) => c.outcome === 'Blocked' || c.outcome === 'Missed');
    const card = make('div', 'stat');
    const title = make('h4', null, npc);
    title.title = npc;
    const rate = make('div', 'rate', released.length ? `${count(own, 'Blocked')} / ${released.length} blocked` : 'none released');
    const dl = make('dl');
    const rowsOf = [
      ['Type', own.some((c) => c.target) ? 'Targeted (raid)' : 'Untargeted (group)'],
      ['Warning', fmtSeconds(median(released.filter((c) => c.warningSeen !== false).map((c) => c.resolvedAt - c.readiedAt)))],
      ['Every', fmtSeconds(median(intervals(own)))],
    ];
    for (const [k, v] of rowsOf) dl.append(make('dt', null, k), make('dd', null, v));
    card.append(title, rate, dl);
    stats.appendChild(card);
  }
}

function showCast(detail, cast, fight) {
  const box = detail.querySelector('.cast-detail');
  box.hidden = false;
  const at = window.AugurTimeline.clock(cast.readiedAt - fight.start);
  const outcome = cast.outcome === 'Unresolved' ? 'No release' : cast.outcome;
  box.querySelector('.cast-title').textContent = `${at} · ${cast.npc}${cast.target ? ` → ${cast.target}` : ''} · ${outcome}`;

  const parts = [];
  if (cast.warningSeen === false) parts.push('released with no warning emote in the log');
  else if (cast.resolvedAt != null && cast.outcome !== 'Cancelled') parts.push(`released ${cast.resolvedAt - cast.readiedAt} s after the warning`);
  else if (cast.outcome === 'Unresolved') parts.push('never released or cancelled');
  const onTime = (cast.bulwarks || []).filter((b) => cast.resolvedAt == null || b.at <= cast.resolvedAt);
  const late = (cast.bulwarks || []).filter((b) => cast.resolvedAt != null && b.at > cast.resolvedAt);
  if (onTime.length) parts.push(`${onTime.length} bulwark${onTime.length > 1 ? 's' : ''} in your group, last ${cast.resolvedAt - onTime[onTime.length - 1].at} s before release`);
  else if (cast.outcome === 'Blocked' || cast.outcome === 'Missed') parts.push('no bulwark seen in your group before release');
  if (late.length) parts.push(`${late.length} bulwark${late.length > 1 ? 's' : ''} pressed ${late[0].at - cast.resolvedAt} s too late`);
  if (cast.splashHits) {
    const players = `${cast.splashPlayers} player${cast.splashPlayers > 1 ? 's' : ''}`;
    const amount = cast.splashDamage > 0 ? ` for ${damage(cast.splashDamage)}` : ', though it did no damage';
    parts.push(`${cast.target ? 'splash' : 'barrage'} hit ${players}${amount}`);
  }
  if (cast.targetHits) parts.push(`target took ${damage(cast.targetDamage)}`);
  box.querySelector('.cast-summary').textContent = `${parts.join('; ')}.`;

  const list = box.querySelector('.cast-lines');
  list.replaceChildren();
  const target = (cast.target || '').toLowerCase();
  for (const line of cast.lines || []) {
    const li = document.createElement('li');
    li.append(make('time', null, time(line.at)), document.createTextNode(line.text));
    if (/releases a barrage|readies a barrage|prepares to unleash|cancels the barrage/.test(line.text)) li.className = 'kind-release';
    else if (/applies a bulwark/.test(line.text)) li.className = 'kind-bulwark';
    else if (/Barrage/.test(line.text)) {
      const onTarget = target && new RegExp(` hits ${escape(target)} `, 'i').test(line.text);
      li.className = `kind-hit${onTarget ? ' on-target' : ''}`;
    }
    list.appendChild(li);
  }
}

function escape(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// ---- update dialog ----

const dialog = $('update-dialog');

function openUpdate(encounter) {
  pendingUpdate = encounter;
  $('update-source').textContent = [encounter.title, encounter.start && when(encounter.start), encounter.zone].filter(Boolean).join(' · ');
  $('update-status').textContent = 'Loading this fight’s NPCs…';
  $('update-status').classList.remove('error');
  $('update-npcs').replaceChildren();
  $('update-preview').textContent = '';
  $('update-save').disabled = true;
  dialog.showModal();
  send({ type: 'encounter-npcs', id: encounter.id });
}

function onEncounterNpcs(data) {
  if (!pendingUpdate || pendingUpdate.id !== data.id || !dialog.open) return;
  pendingUpdate.npcZone = data.zone;
  const status = $('update-status');
  if (data.error) {
    status.textContent = data.error;
    status.classList.add('error');
    return;
  }
  if (!data.npcs.length) {
    status.textContent = 'ACT found no enemy NPCs in this fight.';
    return;
  }
  status.textContent = '';

  // Default to the NPC ACT named the fight after; ACT titles can list several, comma-separated.
  const title = (pendingUpdate.title || '').toLowerCase();
  const titled = new Set([title, ...title.split(', ')]);
  const matches = data.npcs.filter((n) => titled.has(n.name.toLowerCase()));
  const chosen = new Set((matches.length ? matches : data.npcs.slice(0, 1)).map((n) => n.name));

  const list = $('update-npcs');
  list.replaceChildren();
  for (const npc of data.npcs) {
    const li = make('li');
    const label = make('label');
    const box = document.createElement('input');
    box.type = 'checkbox';
    box.value = npc.name;
    box.checked = chosen.has(npc.name);
    box.addEventListener('change', previewUpdate);
    label.append(box, make('span', 'name', npc.name));
    if (npc.damageTaken) label.append(make('span', 'muted', `${damage(npc.damageTaken)} taken`));
    li.appendChild(label);
    list.appendChild(li);
  }
  previewUpdate();
  list.querySelector('input:checked')?.focus();
}

function chosenBosses() {
  return [...$('update-npcs').querySelectorAll('input:checked')].map((box) => box.value);
}

// Says which encounter the save will go to, so picking a different set of bosses is visible as a new entry.
function previewUpdate() {
  const bosses = chosenBosses();
  const all = $('update-npcs').querySelectorAll('input').length;
  $('update-save').disabled = bosses.length === 0;
  const preview = $('update-preview');
  if (!bosses.length) {
    preview.textContent = 'Choose at least one main NPC.';
    return;
  }
  const zone = pendingUpdate.npcZone || pendingUpdate.zone || '(unknown zone)';
  const known = findRecord(zone, bosses);
  const name = [...bosses].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' })).join(', ');
  const adds = all - bosses.length;
  const where = known
    ? `adds a fight to ${name} (recorded ${known.updates} time${known.updates === 1 ? '' : 's'})`
    : `creates a new encounter, ${name}`;
  preview.textContent = `Saving ${where} in ${zone}, with ${adds} add${adds === 1 ? '' : 's'}.`;
}

function findRecord(zone, bosses) {
  const wanted = new Set(bosses.map((b) => b.toLowerCase()));
  return (encounterData?.encounters || []).find((e) => e.zone.toLowerCase() === zone.toLowerCase()
    && e.bosses.length === wanted.size
    && e.bosses.every((b) => wanted.has(b.name.toLowerCase())));
}

// Handled on submit, not on "close": close fires later, possibly after the dialog was opened for another fight.
dialog.querySelector('form').addEventListener('submit', (event) => {
  const encounter = pendingUpdate;
  pendingUpdate = null;
  if (event.submitter?.value !== 'save' || !encounter) return;
  const bosses = chosenBosses();
  if (!bosses.length) {
    event.preventDefault();
    pendingUpdate = encounter;
    return;
  }
  const button = encounter.row.querySelector('.update-data');
  if (button) {
    button.disabled = true;
    button.textContent = 'Updating…';
  }
  send({ type: 'update-encounter', id: encounter.id, bosses });
});

// ---- encounters data ----

function checkbox(checked, label) {
  const box = document.createElement('input');
  box.type = 'checkbox';
  box.checked = !!checked;
  // Shown, not edited: the value only comes from fight logs. Not "disabled", which greys it out.
  box.tabIndex = -1;
  box.setAttribute('aria-readonly', 'true');
  box.setAttribute('aria-label', `${label}: ${checked ? 'yes' : 'no'}`);
  box.addEventListener('click', (event) => event.preventDefault());
  return box;
}

const openRecords = new Set(); // "zone|name" of expanded encounters, kept across re-renders

function renderData() {
  const host = $('data-zones');
  host.replaceChildren();
  if (!encounterData) return;

  $('data-path').textContent = encounterData.path ? `Saved in ${encounterData.path}` : '';
  const filter = $('data-filter').value.trim().toLowerCase();
  const records = encounterData.encounters || [];
  const zones = new Map();
  for (const record of records) {
    const text = [record.zone, record.name, ...record.adds.map((a) => a.name)].join('\n').toLowerCase();
    if (filter && !text.includes(filter)) continue;
    if (!zones.has(record.zone)) zones.set(record.zone, []);
    zones.get(record.zone).push(record);
  }

  for (const [zone, list] of zones) {
    const block = make('details', 'data-zone');
    block.open = true;
    const summary = make('summary', null, zone);
    summary.append(make('span', 'muted', ` · ${list.length} encounter${list.length === 1 ? '' : 's'}`));
    block.appendChild(summary);

    const table = make('table', 'data-table');
    const head = table.createTHead().insertRow();
    for (const [text, cls] of [['Encounter'], ['Barrage', 'center'], ['Adds', 'num'], ['Fights', 'num'], ['Last updated']]) {
      head.appendChild(make('th', cls, text));
    }
    const body = table.createTBody();
    for (const record of list) {
      const key = `${record.zone}|${record.name}`;
      const row = body.insertRow();
      row.className = 'record';
      row.tabIndex = 0;
      const nameCell = row.insertCell();
      nameCell.append(make('span', 'caret'), document.createTextNode(record.name));
      const barrage = row.insertCell();
      barrage.className = 'center';
      barrage.appendChild(checkbox(record.bosses.some((b) => b.releasesBarrage), `${record.name} releases barrages`));
      Object.assign(row.insertCell(), { className: 'num', textContent: String(record.adds.length) });
      Object.assign(row.insertCell(), { className: 'num', textContent: String(record.updates) });
      row.insertCell().textContent = record.updated || '';

      const detail = body.insertRow();
      detail.className = 'record-detail';
      const cell = detail.insertCell();
      cell.colSpan = 5;
      cell.appendChild(npcList('Bosses', record.bosses));
      cell.appendChild(npcList('Adds', record.adds));

      const setOpen = (open) => {
        detail.hidden = !open;
        row.classList.toggle('open', open);
        row.setAttribute('aria-expanded', String(open));
        if (open) openRecords.add(key);
        else openRecords.delete(key);
      };
      setOpen(openRecords.has(key));
      row.addEventListener('click', () => setOpen(detail.hidden));
      row.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          setOpen(detail.hidden);
        }
      });
    }
    block.appendChild(table);
    host.appendChild(block);
  }

  $('data-empty').hidden = zones.size > 0;
  $('data-empty').textContent = records.length
    ? 'No encounter matches the filter.'
    : 'No encounters recorded yet. Use "Update encounters data" on a fight in the Encounters tab.';
}

function npcList(title, npcs) {
  const box = make('div', 'npc-list');
  box.appendChild(make('h4', null, `${title} (${npcs.length})`));
  if (!npcs.length) {
    box.appendChild(make('p', 'muted', 'None recorded.'));
    return box;
  }
  const table = make('table');
  const head = table.createTHead().insertRow();
  head.append(make('th', null, 'Name'), make('th', 'center', 'Barrage'));
  const body = table.createTBody();
  for (const npc of npcs) {
    const row = body.insertRow();
    row.insertCell().textContent = npc.name;
    const cell = row.insertCell();
    cell.className = 'center';
    cell.appendChild(checkbox(npc.releasesBarrage, `${npc.name} releases barrages`));
  }
  box.appendChild(table);
  return box;
}

function onEncounterData(data) {
  encounterData = data;
  renderData();
  if (data.updatedId == null) {
    if (data.error) notice(data.error, true);
    return;
  }

  const encounter = encounters.get(data.updatedId);
  const button = encounter?.row.querySelector('.update-data');
  if (button) {
    button.disabled = false;
    button.textContent = data.error ? 'Update encounters data' : 'Updated';
  }
  if (data.error) {
    notice(data.error, true);
    return;
  }
  const r = data.result;
  const adds = `${r.adds} add${r.adds === 1 ? '' : 's'}`;
  const detail = r.isNew ? `new encounter, ${adds}` : `${adds}${r.addsAdded ? `, ${r.addsAdded} new` : ''}`;
  notice(`Encounters data updated: ${r.name} in ${r.zone} (${detail}).`);
}

$('data-filter').addEventListener('input', renderData);

// ---- tabs ----

function showTab(name) {
  const data = name === 'data';
  $('tab-encounters').setAttribute('aria-selected', String(!data));
  $('tab-data').setAttribute('aria-selected', String(data));
  $('encounters-view').hidden = data;
  $('data-view').hidden = !data;
}

$('tab-encounters').addEventListener('click', () => showTab('encounters'));
$('tab-data').addEventListener('click', () => showTab('data'));

// ---- data sources ----

function onAnalysis(data) {
  const encounter = encounters.get(data.id);
  if (!encounter) return;
  if (data.error) {
    encounter.state = 'error';
    encounter.error = data.error;
  } else {
    const s = data.summary;
    encounter.state = 'done';
    encounter.barrages = s.barrages || [];
    encounter.summaryStart = s.startEpoch;
    encounter.summaryEnd = s.endEpoch;
  }
  fillRow(encounter);
  if (expanded?.encounter === encounter) renderDetail();
}

function onEncounters(data) {
  if (data.replace) {
    collapse();
    encounters.clear();
    rows.replaceChildren();
    notice('');
  }
  // Replacement lists arrive newest first; single additions are newer than everything shown.
  for (const info of data.items) upsert(info, !data.replace);
  const none = encounters.size === 0;
  $('encounters').hidden = none;
  $('empty').hidden = !none;
  $('empty').textContent = 'No encounters in ACT yet. They appear here as fights end.';
}

if (host) {
  for (const node of document.querySelectorAll('.host-only')) node.hidden = false;
  $('refresh').addEventListener('click', () => send({ type: 'list' }));

  host.addEventListener('message', (event) => {
    const data = event.data;
    if (data.type === 'encounters') onEncounters(data);
    else if (data.type === 'analysis') onAnalysis(data);
    else if (data.type === 'encounter-npcs') onEncounterNpcs(data);
    else if (data.type === 'encounter-data') onEncounterData(data);
  });
  send({ type: 'ready' });
}

// Standalone page written by the CLI.
const inline = document.getElementById('augur-data');
if (inline) {
  const report = JSON.parse(inline.textContent);
  $('page-title').textContent = report.title || 'AugurACT';
  document.title = report.title || 'AugurACT';
  const list = report.fights.map((f, i) => upsert({ ...f, id: String(i), barrages: f.barrages }, false));
  if (!list.length) $('empty').textContent = 'No barrages found in these logs.';

  // "#fight=12" opens that fight (0-based, in table order); "&cast=3" also opens that cast.
  const wanted = Number((location.hash.match(/fight=(\d+)/) || [])[1] || 0);
  const chosen = list[Math.min(wanted, list.length - 1)];
  if (chosen) {
    toggle(chosen);
    chosen.row.scrollIntoView({ block: 'start' });
    const cast = (location.hash.match(/cast=(\d+)/) || [])[1];
    const pick = cast != null && chosen.barrages[Number(cast)];
    if (pick && expanded?.timeline) {
      expanded.timeline.select(pick);
      const { start, end } = span(chosen);
      showCast(expanded.detail, pick, { title: chosen.title, start, end, barrages: chosen.barrages });
    }
  }
} else if (!host) {
  $('empty').textContent = 'Open this page from the ACT plugin tab.';
}
