'use strict';

// Fights come from the plugin (one "encounter-summary" message per finished ACT encounter) or, in the page
// the CLI writes, from an inline "barrage-report". Both carry barrage casts in the same shape.

const fights = [];
const fightRows = document.querySelector('#fights tbody');
let timeline = null;
let selectedRow = null;

const $ = (id) => document.getElementById(id);

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

function addFight(fight) {
  fights.push(fight);
  const casts = fight.barrages || [];
  const row = fightRows.insertRow(fight.prepend ? 0 : -1);
  const cells = [
    [fight.title],
    [fight.zone || ''],
    [fight.start ? when(fight.start) : ''],
    [fight.start && fight.end ? duration(fight.end - fight.start) : '', 'num'],
    [casts.length || '', 'num'],
    [count(casts, 'Blocked') || '', 'num'],
    [count(casts, 'Missed') || '', 'num missed-count'],
    [count(casts, 'Cancelled') || '', 'num'],
  ];
  for (const [text, cls] of cells) {
    const cell = row.insertCell();
    cell.textContent = text;
    if (cls) cell.className = cls;
  }
  if (fight.source) row.title = fight.source;

  if (casts.length) {
    row.addEventListener('click', () => select(fight, row));
  } else {
    row.classList.add('inactive');
    row.title = 'No barrage in this encounter';
  }

  $('fights-section').hidden = false;
  $('empty').hidden = true;
  return row;
}

function select(fight, row) {
  selectedRow?.classList.remove('selected');
  selectedRow = row;
  row.classList.add('selected');
  row.scrollIntoView({ block: 'nearest' });

  $('detail').hidden = false;
  $('detail-title').textContent = fight.title;
  $('detail-zone').textContent = [fight.zone, fight.start && when(fight.start), fight.source].filter(Boolean).join(' · ');
  renderStats(fight.barrages);
  $('cast-detail').hidden = true;

  timeline?.dispose();
  timeline = window.AugurTimeline.create($('timeline'), fight, (cast) => showCast(cast, fight));
}

function renderStats(casts) {
  const byNpc = new Map();
  for (const c of casts) {
    if (!byNpc.has(c.npc)) byNpc.set(c.npc, []);
    byNpc.get(c.npc).push(c);
  }

  const host = $('boss-stats');
  host.replaceChildren();
  for (const [npc, list] of byNpc) {
    const released = list.filter((c) => c.outcome === 'Blocked' || c.outcome === 'Missed');
    const blocked = count(list, 'Blocked');
    const card = document.createElement('div');
    card.className = 'stat';

    const title = document.createElement('h4');
    title.textContent = npc;
    title.title = npc;
    const rate = document.createElement('div');
    rate.className = 'rate';
    rate.textContent = released.length ? `${blocked} / ${released.length} blocked` : 'none released';

    const facts = [
      ['Type', list.some((c) => c.target) ? 'Targeted (raid)' : 'Untargeted (group)'],
      ['Missed', String(count(list, 'Missed'))],
      ['Cancelled', String(count(list, 'Cancelled'))],
      ['Warning', fmtSeconds(median(released.filter((c) => c.warningSeen !== false).map((c) => c.resolvedAt - c.readiedAt)))],
      ['Every', fmtSeconds(median(intervals(list)))],
    ];
    const ability = list.find((c) => c.damageAbility)?.damageAbility;
    if (ability) facts.push(['Damage line', ability]);

    const dl = document.createElement('dl');
    for (const [k, v] of facts) {
      const dt = document.createElement('dt');
      dt.textContent = k;
      const dd = document.createElement('dd');
      dd.textContent = v;
      dl.append(dt, dd);
    }
    card.append(title, rate, dl);
    host.appendChild(card);
  }
}

function fmtSeconds(value) {
  return value == null ? '-' : `${Math.round(value * 10) / 10} s`;
}

function showCast(cast, fight) {
  const box = $('cast-detail');
  box.hidden = false;
  const at = window.AugurTimeline.clock(cast.readiedAt - fight.start);
  $('cast-title').textContent = `${at} · ${cast.npc}${cast.target ? ` → ${cast.target}` : ''} · ${cast.outcome}`;

  const parts = [];
  if (cast.warningSeen === false) parts.push('released with no warning emote in the log');
  else if (cast.resolvedAt != null && cast.outcome !== 'Cancelled') parts.push(`released ${cast.resolvedAt - cast.readiedAt} s after the warning`);
  const onTime = (cast.bulwarks || []).filter((b) => cast.resolvedAt == null || b.at <= cast.resolvedAt);
  const late = (cast.bulwarks || []).filter((b) => cast.resolvedAt != null && b.at > cast.resolvedAt);
  if (onTime.length) parts.push(`${onTime.length} bulwark${onTime.length > 1 ? 's' : ''} in your group, last ${cast.resolvedAt - onTime[onTime.length - 1].at} s before release`);
  else if (cast.outcome !== 'Cancelled') parts.push('no bulwark seen in your group before release');
  if (late.length) parts.push(`${late.length} pressed ${late[0].at - cast.resolvedAt} s too late`);
  if (cast.splashHits) parts.push(`${cast.target ? 'splash' : 'barrage'} hit ${cast.splashPlayers} player${cast.splashPlayers > 1 ? 's' : ''} for ${damage(cast.splashDamage)}`);
  if (cast.targetHits) parts.push(`target took ${damage(cast.targetDamage)}`);
  $('cast-summary').textContent = `${parts.join('; ')}.`;

  const list = $('cast-lines');
  list.replaceChildren();
  const target = (cast.target || '').toLowerCase();
  for (const line of cast.lines || []) {
    const li = document.createElement('li');
    const stamp = document.createElement('time');
    stamp.textContent = time(line.at);
    li.append(stamp, document.createTextNode(line.text));
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

$('reset-zoom').addEventListener('click', () => timeline?.reset());

// Standalone page written by the CLI.
const inline = document.getElementById('augur-data');
if (inline) {
  const report = JSON.parse(inline.textContent);
  $('page-title').textContent = report.title || 'AugurACT';
  document.title = report.title || 'AugurACT';
  const rows = report.fights.map(addFight);
  if (!report.fights.length) $('empty').textContent = 'No barrages found in these logs.';

  // "#fight=12" opens that fight (0-based, in table order); otherwise the first one.
  const wanted = Number((location.hash.match(/fight=(\d+)/) || [])[1] || 0);
  const index = Math.min(wanted, rows.length - 1);
  if (index >= 0) {
    select(report.fights[index], rows[index]);
    const cast = (location.hash.match(/cast=(\d+)/) || [])[1];
    const chosen = cast != null && report.fights[index].barrages[Number(cast)];
    if (chosen) {
      timeline.select(chosen);
      showCast(chosen, report.fights[index]);
    }
  }
}

// Inside ACT.
window.chrome?.webview?.addEventListener('message', (event) => {
  const data = event.data;
  if (data.type !== 'encounter-summary') return;
  const casts = data.barrages || [];
  addFight({
    title: data.title,
    zone: data.zone,
    start: casts.length ? Math.min(data.startEpoch ?? Infinity, casts[0].readiedAt) : data.startEpoch,
    end: data.endEpoch,
    barrages: casts,
    prepend: true,
  });
});
