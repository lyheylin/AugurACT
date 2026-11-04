'use strict';

// The page: the encounter list, its expanded detail (timeline, filter, legend, side panels), the update
// dialog and the encounters data tab. Nothing here is about one mechanic: columns, panels, marks, legend
// entries and detail text all come from the mechanics registered in web/mechanics/ (AugurTimeline.mechanics).
//
// Encounters come from the plugin (ACT's encounter list, each analyzed on request into an EncounterSummary)
// or, in the page the CLI writes, from an inline "timeline-report" whose fights carry the same summary.
//
// Log text holds player names and chat, so everything from it is written with textContent.

const { make, damage, plural } = window.AugurUtil;
const mechanics = window.AugurTimeline.mechanics;
const host = window.chrome?.webview;
const encounters = new Map(); // id -> encounter
const rows = document.querySelector('#encounters tbody');
let expanded = null; // { encounter, row, detailRow, detail, timeline }
const panelOpen = new Map(); // panel name -> open; kept while switching encounters
const markerShown = new Map(); // mechanic key -> marks shown; kept while switching encounters
let encounterData = null;
let pendingUpdate = null; // encounter the update dialog is open for

const $ = (id) => document.getElementById(id);

// ---- formatting ----

function when(epoch) {
  const d = new Date(epoch * 1000);
  return `${d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} ${d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}`;
}

function duration(seconds) {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
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

const FIXED_COLUMNS = 4; // encounter, zone, when, length
const columns = mechanics.flatMap((m) => m.columns || []);
const COLUMNS = FIXED_COLUMNS + columns.length + 1; // and the host-only action

// The mechanics' count columns go between the fixed ones and the action button.
function buildHeader() {
  const action = document.querySelector('#encounters thead th.host-only');
  for (const column of columns) action.before(make('th', 'num', column.header));
}

// Timeline origin and end: ACT's start and end, widened to any mechanic event before or after them.
function span(encounter) {
  let start = encounter.start || encounter.summary?.startEpoch || 0;
  let end = encounter.end || encounter.summary?.endEpoch || start;
  if (encounter.summary) {
    for (const t of mechanics.flatMap((m) => (m.times ? m.times(encounter.summary) : []))) {
      if (!start || t < start) start = t;
      end = Math.max(end, t);
    }
  }
  return { start, end };
}

function fightOf(encounter) {
  const { start, end } = span(encounter);
  return { title: encounter.title, start, end, summary: encounter.summary };
}

function upsert(info, prepend) {
  const existing = encounters.get(info.id);
  if (existing) {
    Object.assign(existing, info);
    fillRow(existing);
    return existing;
  }

  const encounter = { ...info, summary: null, state: 'pending' };
  encounters.set(encounter.id, encounter);

  const row = document.createElement('tr');
  row.className = 'encounter';
  row.tabIndex = 0;
  row.setAttribute('aria-expanded', 'false');
  for (let i = 0; i < COLUMNS; i++) row.appendChild(document.createElement('td'));
  row.cells[3].className = 'num';
  columns.forEach((column, i) => { row.cells[FIXED_COLUMNS + i].className = `num ${column.className || ''}`.trim(); });
  const action = row.cells[COLUMNS - 1];
  action.className = 'host-only action';
  action.hidden = !host;

  if (host) {
    const button = make('button', 'update-data', 'Update encounters data');
    button.type = 'button';
    button.title = 'Record this fight in the encounters data, choosing its main NPC';
    button.addEventListener('click', (event) => {
      event.stopPropagation();
      openUpdate(encounter);
    });
    action.appendChild(button);
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
  const { row, summary } = encounter;
  const { start, end } = span(encounter);
  const waiting = encounter.state === 'pending' ? '…' : encounter.state === 'error' ? '?' : '';
  const cells = [
    encounter.title,
    encounter.zone || '',
    start ? when(start) : '',
    start && end > start ? duration(end - start) : '',
    ...columns.map((column) => (summary ? column.value(summary) || '' : waiting)),
  ];
  cells.forEach((text, i) => { row.cells[i].textContent = text; });
  row.cells[0].title = encounter.title;
  row.cells[1].title = encounter.zone || '';
  row.classList.toggle('pending', encounter.state === 'pending');
  row.title = encounter.state === 'error' ? `${encounter.error} Click to try again.` : encounter.source || '';
}

// ---- encounter detail ----

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
  buildFilter(detail);
  buildLegend(detail);
  buildPanels(detail);

  expanded = { encounter, row: encounter.row, detailRow, detail, timeline: null };
  renderDetail();

  // Background analysis may not have reached this one yet, or it failed; ask for it now.
  if (encounter.state !== 'done' && host) {
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

function renderDetail() {
  if (!expanded) return;
  const { encounter, detail } = expanded;
  const message = detail.querySelector('.detail-message');
  const timelineHost = detail.querySelector('.timeline');
  const extras = [detail.querySelector('.marker-filter'), detail.querySelector('.legend')];
  const { summary } = encounter;

  expanded.timeline?.dispose();
  expanded.timeline = null;
  detail.querySelector('.cast-detail').hidden = true;
  renderPanels(detail, summary);

  const fail = (text) => {
    message.textContent = text;
    message.hidden = false;
    timelineHost.hidden = true;
    for (const node of extras) node.hidden = true;
  };
  if (encounter.state === 'error') return fail(encounter.error);
  if (!summary) return fail('Analyzing this encounter…');
  const fight = fightOf(encounter);
  const lanes = window.AugurTimeline.lanesOf(summary);
  if (!lanes.npcs.length && !lanes.players.length) return fail('Nothing to show for this encounter.');

  message.hidden = true;
  timelineHost.hidden = false;
  for (const node of extras) node.hidden = false;
  expanded.timeline = window.AugurTimeline.create(timelineHost, fight, (mechanic, item) => showItem(detail, mechanic, item, fight), isShown);
}

function showItem(detail, mechanic, item, fight) {
  const box = detail.querySelector('.cast-detail');
  box.hidden = false;
  mechanic.showDetail({
    title: box.querySelector('.cast-title'),
    summary: box.querySelector('.cast-summary'),
    lines: box.querySelector('.cast-lines'),
  }, item, fight);
}

// ---- timeline filter and legend ----

function isShown(key) {
  return markerShown.get(key) ?? true;
}

// One switch per mechanic, turning all its marks on or off together.
function buildFilter(detail) {
  const filter = detail.querySelector('.marker-filter');
  for (const mechanic of mechanics) {
    if (!mechanic.draw) continue;
    const label = make('label', 'marker-option');
    label.dataset.marker = mechanic.key;
    const box = document.createElement('input');
    box.type = 'checkbox';
    box.checked = isShown(mechanic.key);
    box.addEventListener('change', () => {
      markerShown.set(mechanic.key, box.checked);
      expanded?.timeline?.refresh();
    });
    label.append(box, make('i', mechanic.swatch), make('span', null, mechanic.label));
    filter.appendChild(label);
  }
}

function buildLegend(detail) {
  const legend = detail.querySelector('.legend');
  for (const entry of mechanics.flatMap((m) => m.legend || [])) {
    const item = make('span');
    item.append(make('i', entry.swatch), document.createTextNode(entry.label));
    legend.appendChild(item);
  }
}

// ---- side panels: one folding panel per mechanic ----

function buildPanels(detail) {
  const side = detail.querySelector('.side');
  for (const mechanic of mechanics) {
    if (!mechanic.panel) continue;
    const panel = make('section', 'side-panel');
    panel.dataset.panel = mechanic.key;
    const heading = make('h3');
    const toggleButton = make('button', 'side-toggle', mechanic.panel.title);
    toggleButton.type = 'button';
    toggleButton.addEventListener('click', () => {
      panelOpen.set(mechanic.key, !(panelOpen.get(mechanic.key) ?? true));
      applyPanel(panel);
    });
    heading.appendChild(toggleButton);
    panel.append(heading, make('div', 'side-body'));
    side.appendChild(panel);
    applyPanel(panel);
  }
}

function applyPanel(panel) {
  const open = panelOpen.get(panel.dataset.panel) ?? true;
  panel.classList.toggle('closed', !open);
  panel.querySelector('.side-toggle').setAttribute('aria-expanded', String(open));
}

function renderPanels(detail, summary) {
  for (const mechanic of mechanics) {
    if (!mechanic.panel) continue;
    const body = detail.querySelector(`.side-panel[data-panel="${mechanic.key}"] .side-body`);
    mechanic.panel.render(body, summary);
  }
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
  const where = known
    ? `adds a fight to ${name} (recorded ${plural(known.updates, 'time')})`
    : `creates a new encounter, ${name}`;
  preview.textContent = `Saving ${where} in ${zone}, with ${plural(all - bosses.length, 'add')}.`;
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

// Mechanics that record a yes/no per NPC in the encounters data (EncounterStore's NpcRecord).
const dataFlags = mechanics.filter((m) => m.dataFlag).map((m) => m.dataFlag);

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
  const zonesHost = $('data-zones');
  zonesHost.replaceChildren();
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

  const headers = [['Encounter'], ...dataFlags.map((f) => [f.header, 'center']), ['Adds', 'num'], ['Fights', 'num'], ['Last updated']];
  for (const [zone, list] of zones) {
    const block = make('details', 'data-zone');
    block.open = true;
    const summary = make('summary', null, zone);
    summary.append(make('span', 'muted', ` · ${plural(list.length, 'encounter')}`));
    block.appendChild(summary);

    const table = make('table', 'data-table');
    const head = table.createTHead().insertRow();
    for (const [text, cls] of headers) head.appendChild(make('th', cls, text));
    const body = table.createTBody();
    for (const record of list) {
      const key = `${record.zone}|${record.name}`;
      const row = body.insertRow();
      row.className = 'record';
      row.tabIndex = 0;
      row.insertCell().append(make('span', 'caret'), document.createTextNode(record.name));
      for (const flag of dataFlags) {
        const cell = row.insertCell();
        cell.className = 'center';
        cell.appendChild(checkbox(record.bosses.some((b) => b[flag.field]), flag.describe(record.name)));
      }
      Object.assign(row.insertCell(), { className: 'num', textContent: String(record.adds.length) });
      Object.assign(row.insertCell(), { className: 'num', textContent: String(record.updates) });
      row.insertCell().textContent = record.updated || '';

      const detail = body.insertRow();
      detail.className = 'record-detail';
      const cell = detail.insertCell();
      cell.colSpan = headers.length;
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
    zonesHost.appendChild(block);
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
  head.appendChild(make('th', null, 'Name'));
  for (const flag of dataFlags) head.appendChild(make('th', 'center', flag.header));
  const body = table.createTBody();
  for (const npc of npcs) {
    const row = body.insertRow();
    row.insertCell().textContent = npc.name;
    for (const flag of dataFlags) {
      const cell = row.insertCell();
      cell.className = 'center';
      cell.appendChild(checkbox(npc[flag.field], flag.describe(npc.name)));
    }
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
  const adds = plural(r.adds, 'add');
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

function applySummary(encounter, summary) {
  encounter.state = 'done';
  encounter.summary = summary;
  fillRow(encounter);
  if (expanded?.encounter === encounter) renderDetail();
}

function onAnalysis(data) {
  const encounter = encounters.get(data.id);
  if (!encounter) return;
  if (data.error) {
    encounter.state = 'error';
    encounter.error = data.error;
    fillRow(encounter);
    if (expanded?.encounter === encounter) renderDetail();
    return;
  }
  applySummary(encounter, data.summary);
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

buildHeader();

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

// Standalone page written by the CLI's timeline command.
const inline = document.getElementById('augur-data');
if (inline) {
  const report = JSON.parse(inline.textContent);
  $('page-title').textContent = report.title || 'AugurACT';
  document.title = report.title || 'AugurACT';
  const list = report.fights.map((f, i) => {
    const encounter = upsert({ id: String(i), title: f.title, zone: f.zone, start: f.start, end: f.end, source: f.source }, false);
    applySummary(encounter, f.summary);
    return encounter;
  });
  if (!list.length) $('empty').textContent = 'No fights with mechanics found in these logs.';

  // "#fight=12" opens that fight (0-based, in table order); "&select=barrage:3" also opens that mechanic's
  // item; "&hide=barrage,death" switches those marks off.
  const hash = (name) => (location.hash.match(new RegExp(`${name}=([\\w:,-]+)`)) || [])[1];
  const chosen = list[Math.min(Number(hash('fight') || 0), list.length - 1)];
  if (chosen) {
    for (const key of (hash('hide') || '').split(',').filter(Boolean)) markerShown.set(key, false);
    toggle(chosen);
    chosen.row.scrollIntoView({ block: 'start' });
    const [key, index] = (hash('select') || '').split(':');
    const mechanic = mechanics.find((m) => m.key === key);
    const item = mechanic?.items?.(chosen.summary)[Number(index)];
    if (item && expanded?.timeline) {
      expanded.timeline.select(item);
      showItem(expanded.detail, mechanic, item, fightOf(chosen));
    }
  }
} else if (!host) {
  $('empty').textContent = 'Open this page from the ACT plugin tab.';
}
