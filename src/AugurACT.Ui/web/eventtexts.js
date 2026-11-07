'use strict';

// Event texts outside the timeline: the template format, the dialog that adds or edits an encounter's event text
// (with its description and timer) or blacklists one, each encounter's list on the Encounters data tab, and the
// Blacklist tab. The timeline marks and the list under the timeline are in mechanics/eventtext.js.
//
// A template is the text with variables written as {player}, {class}, {archetype} or {number}, and a literal
// brace doubled. It must read the same as EventTextTemplate in Core, which uses it to apply the blacklist.
window.AugurEventTexts = (() => {
  const { make, plural, clock } = window.AugurUtil;

  const KINDS = { emote: 'Emote', say: 'NPC say', narration: 'Narration' };
  const SLOTS = ['player', 'class', 'archetype', 'number'];
  const ARCHETYPES = ['Fighter', 'Priest', 'Scout', 'Mage'];
  const CLASSES = [
    'Guardian', 'Berserker', 'Monk', 'Bruiser', 'Shadowknight', 'Paladin',
    'Templar', 'Inquisitor', 'Warden', 'Fury', 'Mystic', 'Defiler', 'Channeler',
    'Swashbuckler', 'Brigand', 'Troubadour', 'Dirge', 'Ranger', 'Assassin', 'Beastlord',
    'Wizard', 'Warlock', 'Illusionist', 'Coercer', 'Conjuror', 'Necromancer',
  ];
  const NUMBER = '\\d+(?:[.,]\\d+)*[KMBTQ]?';
  const PATTERNS = { player: '[A-Za-z]+', number: NUMBER, class: CLASSES.join('|'), archetype: ARCHETYPES.join('|') };

  // Parts of plain text that could be variables. Player names can't be told from other words here; the user
  // marks them, or they come from the encounter's player list.
  const CANDIDATE = new RegExp(
    `(?<![A-Za-z0-9])(?:(?<class>${PATTERNS.class})|(?<archetype>${PATTERNS.archetype})|(?<number>${NUMBER}))(?![A-Za-z0-9])`, 'gi');

  let deps = null; // { send, host, getData }

  // ---- template format ----

  const escapeLiteral = (text) => text.replace(/\{/g, '{{').replace(/\}/g, '}}');
  const escapeRegex = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

  // [{ text } | { slot }], or { error }.
  function parse(template) {
    if (!template || !template.trim()) return { error: 'The text is empty.' };
    const parts = [];
    let literal = '';
    const token = /\{\{|\}\}|\{([a-z]+)\}|[{}]/g;
    let at = 0;
    let m;
    while ((m = token.exec(template))) {
      literal += template.slice(at, m.index);
      at = token.lastIndex;
      if (m[0] === '{{' || m[0] === '}}') { literal += m[0][0]; continue; }
      if (!SLOTS.includes(m[1])) {
        return { error: m[0].length === 1 ? 'A single { or } must be doubled, as {{ or }}.' : `Unknown variable ${m[0]}. Use {player}, {class}, {archetype} or {number}.` };
      }
      if (literal) parts.push({ text: literal });
      literal = '';
      parts.push({ slot: m[1] });
    }
    literal += template.slice(at);
    if (literal) parts.push({ text: literal });
    return { parts };
  }

  // Matches whole lines without markup, ignoring case, like EventTextTemplate.ToRegex.
  function toRegex(template) {
    const parsed = parse(template);
    if (parsed.error) return null;
    return new RegExp(`^${parsed.parts.map((p) => (p.slot ? `(${PATTERNS[p.slot]})` : escapeRegex(p.text))).join('')}$`, 'i');
  }

  // The template drawn with its variables as chips.
  function render(template) {
    const node = make('span', 'et-text');
    const parsed = parse(template);
    if (parsed.error) {
      node.textContent = template;
      return node;
    }
    for (const part of parsed.parts) {
      node.appendChild(part.slot ? make('span', 'et-var', `{${part.slot}}`) : document.createTextNode(part.text));
    }
    return node;
  }

  function kindTag(kind) {
    const tag = make('span', `et-kind et-${kind}`);
    tag.append(make('i', `et-swatch et-${kind}`), document.createTextNode(KINDS[kind] || kind));
    return tag;
  }

  // ---- timers ----

  // "3:40 ± 13 s", or "20 s ± 2 s".
  function formatTimer(mean, spread) {
    const m = mean >= 60 ? clock(mean) : `${Math.round(mean)} s`;
    return `${m} ± ${Math.round(spread)} s`;
  }

  function describeRecorded(timer) {
    const fights = timer.fights?.length || 0;
    return fights ? `from ${plural(fights, 'fight')}, ${plural(timer.intervals, 'interval')}` : 'typed by hand';
  }

  // "3:40", "220" or "220.5" seconds; NaN when unreadable.
  function readSeconds(text) {
    const value = (text || '').trim();
    const m = value.match(/^(\d+):(\d{1,2}(?:\.\d+)?)$/);
    if (m) return Number(m[1]) * 60 + Number(m[2]);
    return /^\d+(?:\.\d+)?$/.test(value) ? Number(value) : NaN;
  }

  const secondsText = (value) => (value >= 60 ? clock(value) : String(Math.round(value * 10) / 10));

  // A recorded timer with one more fight's measurement in it, weighted by intervals. The ± widens to cover
  // both; a timer typed by hand counts as one interval.
  function mergeTimer(recorded, measured, fight) {
    const fresh = { mean: measured.mean, spread: measured.spread, intervals: measured.events - 1, fights: [fight] };
    if (!recorded) return fresh;
    if ((recorded.fights || []).includes(fight)) return { ...recorded };
    const n = recorded.intervals || 1;
    const m = fresh.intervals;
    const mean = (recorded.mean * n + measured.mean * m) / (n + m);
    const lo = Math.min(recorded.mean - recorded.spread, measured.min);
    const hi = Math.max(recorded.mean + recorded.spread, measured.max);
    return {
      mean: Math.round(mean * 10) / 10,
      spread: Math.round(Math.max(mean - lo, hi - mean) * 10) / 10,
      intervals: recorded.intervals + m,
      fights: [...(recorded.fights || []), fight],
    };
  }

  // ---- matching ACT fights to recorded encounters ----

  // Recorded encounters in the fight's zone whose bosses appear in it, best first.
  function matchRecords(encounter, summary) {
    const records = deps?.getData()?.encounters || [];
    const zone = (encounter.zone || '').toLowerCase();
    const names = new Set((encounter.title || '').toLowerCase().split(', '));
    for (const c of summary?.barrages || []) names.add(c.npc.toLowerCase());
    for (const g of summary?.eventTexts || []) for (const o of g.occurrences) if (o.npc) names.add(o.npc.toLowerCase());
    return records
      .filter((r) => r.zone.toLowerCase() === zone)
      .map((r) => ({ record: r, hits: r.bosses.filter((b) => names.has(b.name.toLowerCase())).length }))
      .filter((m) => m.hits > 0)
      .sort((a, b) => (b.hits === b.record.bosses.length) - (a.hits === a.record.bosses.length) || b.hits - a.hits)
      .map((m) => m.record);
  }

  // The recorded entry that already covers a group from this fight, if any.
  function recordedEntry(record, group) {
    for (const entry of record?.eventTexts || []) {
      const rule = toRegex(entry.text);
      if (rule && group.occurrences.some((o) => rule.test(o.text))) return entry;
    }
    return null;
  }

  // ---- the dialog ----

  const dialog = document.getElementById('text-dialog');
  const field = (id) => document.getElementById(id);
  // { mode: 'add' | 'edit' | 'blacklist', group, fight, record, entry, parts, timerBase }
  let state = null;

  // Editor parts: [{ text } | { slot, variable, value?, values? }]. The input shows the template they make.
  function templateOf(parts) {
    return parts.map((p) => (p.slot ? (p.variable ? `{${p.slot}}` : escapeLiteral(p.value)) : escapeLiteral(p.text))).join('');
  }

  // Reads the input back into parts: placeholders are variables; classes, archetypes and numbers in the
  // literal text become parts that can be switched to variables. Values seen before are kept by position.
  function partsFromInput(template, previous) {
    const parsed = parse(template);
    if (parsed.error) return parsed;
    const parts = [];
    for (const part of parsed.parts) {
      if (part.slot) {
        parts.push({ slot: part.slot, variable: true });
        continue;
      }
      let at = 0;
      for (const m of part.text.matchAll(CANDIDATE)) {
        if (m.index > at) parts.push({ text: part.text.slice(at, m.index) });
        const slot = SLOTS.find((s) => m.groups[s] !== undefined);
        parts.push({ slot, variable: false, value: m[0] });
        at = m.index + m[0].length;
      }
      if (at < part.text.length) parts.push({ text: part.text.slice(at) });
    }
    const old = (previous || []).filter((p) => p.slot);
    parts.filter((p) => p.slot).forEach((p, i) => {
      const was = old[i];
      if (was && was.slot === p.slot) {
        p.value = p.value ?? was.value;
        p.values = was.values;
      }
    });
    return { parts };
  }

  function show(parts, error) {
    state.parts = parts;
    const chips = field('text-chips');
    chips.replaceChildren();
    const seen = field('text-seen');
    seen.replaceChildren();
    field('text-error').textContent = error || '';
    if (!parts) {
      updateSave();
      return;
    }

    parts.forEach((part) => {
      if (!part.slot) {
        chips.appendChild(document.createTextNode(part.text));
        return;
      }
      const chip = make('button', `et-chip${part.variable ? ' variable' : ''}`, part.variable ? `{${part.slot}}` : part.value);
      chip.type = 'button';
      const canToggle = part.slot !== 'player' && (part.variable ? part.value != null : true);
      chip.disabled = !canToggle;
      chip.title = part.slot === 'player'
        ? 'Player names are always variables.'
        : part.variable
          ? (part.value != null ? `Click to fix this to "${part.value}".` : 'Typed as a variable; edit the text to change it.')
          : `Click to make this a {${part.slot}} variable.`;
      chip.addEventListener('click', () => {
        part.variable = !part.variable;
        field('text-input').value = templateOf(parts);
        show(parts);
      });
      chips.appendChild(chip);
    });

    // What each kind of variable was seen as, in this fight.
    const byKind = new Map();
    for (const part of parts) {
      if (!part.slot || !part.values?.length) continue;
      if (!byKind.has(part.slot)) byKind.set(part.slot, new Set());
      for (const v of part.values) byKind.get(part.slot).add(v);
    }
    for (const [slot, values] of byKind) {
      const row = make('div');
      row.append(make('span', 'et-var', `{${slot}}`), document.createTextNode(` seen as ${[...values].slice(0, 12).join('; ')}${values.size > 12 ? '; …' : ''}`));
      seen.appendChild(row);
    }
    updateSave();
  }

  function chosenRecord() {
    if (state.mode === 'edit') return state.record;
    const value = field('text-encounter').value;
    if (!value) return null;
    const [zone, name] = JSON.parse(value);
    return (deps.getData()?.encounters || []).find((r) => r.zone === zone && r.name === name) || null;
  }

  // The timer the save would write, or { error }.
  function timerFromInputs() {
    const mean = readSeconds(field('text-timer-mean').value);
    const spread = readSeconds(field('text-timer-spread').value || '0');
    if (!(mean > 0) || !(spread >= 0)) return { error: 'Write the timer as seconds or m:ss, like 220 or 3:40, and the ± in seconds.' };
    const base = state.timerBase;
    // Changed fields count as typed by hand. Compared as text, since the fields show rounded values.
    const same = base && field('text-timer-mean').value === state.timerShown.mean && field('text-timer-spread').value === state.timerShown.spread;
    return same ? { ...base } : { mean, spread, intervals: 0, fights: [] };
  }

  function updateSave() {
    const template = field('text-input').value.trim();
    const parsed = parse(template);
    const save = field('text-save');
    let problem = parsed.error || '';
    const entering = state.mode !== 'blacklist';
    if (!problem && state.mode === 'add' && !field('text-encounter').value) problem = 'Choose the encounter to add it to.';
    const timerOn = entering && field('text-timer-on').checked;
    field('text-timer-values').classList.toggle('off', !timerOn);
    if (!problem && timerOn && timerFromInputs().error) problem = timerFromInputs().error;
    save.disabled = !!problem;
    field('text-error').textContent = problem;

    // Lines from this fight the text would match, as a check on the variables.
    const check = field('text-check');
    check.textContent = '';
    if (!parsed.error && state.group) {
      const rule = toRegex(template);
      const n = state.group.occurrences.filter((o) => rule.test(o.text)).length;
      check.textContent = `Matches ${n} of ${plural(state.group.occurrences.length, 'line')} in this fight.`;
      check.classList.toggle('error', n === 0);
    }
  }

  // Fills the description and timer from the entry the chosen encounter already has, and this fight's timer.
  function prefill() {
    if (state.mode === 'blacklist') return;
    const record = chosenRecord();
    const entry = state.mode === 'edit'
      ? state.entry
      : state.group
        ? recordedEntry(record, state.group)
        : (record?.eventTexts || []).find((e) => e.text === field('text-input').value.trim()) || null;
    state.entry = entry;
    if (state.mode !== 'edit') field('text-save').textContent = entry ? 'Update' : 'Add';
    field('text-existing').textContent = entry && state.mode !== 'edit' ? `${record.name} already lists this as "${entry.text}". Saving updates it.` : '';
    field('text-description').value = entry?.description || '';

    const recorded = entry?.timer || null;
    const measured = state.group?.timer?.steady ? state.group.timer : null;
    let base = null;
    let checked = false;
    let note;
    if (state.mode === 'edit') {
      base = recorded;
      checked = !!recorded;
      note = recorded ? `Recorded ${describeRecorded(recorded)}. Untick to remove it.` : 'No timer recorded. Tick to type one.';
    } else if (measured && recorded) {
      base = mergeTimer(recorded, measured, state.fight);
      checked = true;
      note = (recorded.fights || []).includes(state.fight)
        ? `This fight is already in the recorded timer (${describeRecorded(recorded)}).`
        : `Recorded ${formatTimer(recorded.mean, recorded.spread)} ${describeRecorded(recorded)}; this fight ${formatTimer(measured.mean, measured.spread)}. Combined above. Untick to keep the recorded one.`;
    } else if (measured) {
      base = mergeTimer(null, measured, state.fight);
      checked = true;
      note = `Measured in this fight: ${plural(measured.events, 'event')}, ${measured.min}–${measured.max} s apart.`;
    } else if (recorded) {
      base = recorded;
      note = `Recorded ${formatTimer(recorded.mean, recorded.spread)} ${describeRecorded(recorded)}. Tick to change it.`;
    } else {
      note = state.group ? 'Not on a steady timer in this fight. Tick to type one.' : 'Tick to type a timer.';
    }
    state.timerBase = base;
    field('text-timer-on').checked = checked;
    field('text-timer-label').textContent = state.mode === 'edit' ? 'Timer' : recorded ? 'Update timer' : 'Attach timer';
    field('text-timer-mean').value = base ? secondsText(base.mean) : '';
    field('text-timer-spread').value = base ? String(Math.round(base.spread * 10) / 10) : '';
    state.timerShown = { mean: field('text-timer-mean').value, spread: field('text-timer-spread').value };
    field('text-timer-note').textContent = note;
  }

  function fillEncounters(chosen, matches) {
    const select = field('text-encounter');
    select.replaceChildren();
    const records = deps.getData()?.encounters || [];
    if (!chosen) select.appendChild(new Option('Choose an encounter…', ''));
    const zones = new Map();
    for (const r of records) {
      if (!zones.has(r.zone)) zones.set(r.zone, []);
      zones.get(r.zone).push(r);
    }
    const ordered = [...zones].sort(([a], [b]) => (matches?.[0]?.zone === b) - (matches?.[0]?.zone === a));
    for (const [zone, listed] of ordered) {
      const group = document.createElement('optgroup');
      group.label = zone;
      for (const r of listed) {
        const option = new Option(r.name, JSON.stringify([r.zone, r.name]));
        option.selected = r === chosen;
        group.appendChild(option);
      }
      select.appendChild(group);
    }
    field('text-encounter-hint').textContent = records.length
      ? (chosen || !matches ? '' : 'This fight’s encounter isn’t in the encounters data yet. Use "Update encounters data" first, or choose one.')
      : 'No encounters recorded yet. Use "Update encounters data" on a fight first.';
  }

  // options: { mode: 'add' | 'edit' | 'blacklist', group?, kind?, text?, record?, entry?, encounter?, summary? }
  function open(options) {
    if (!deps?.host) return;
    const { mode, group } = options;
    state = {
      mode,
      group: group || null,
      record: options.record || null,
      entry: options.entry || null,
      fight: options.summary?.startEpoch ?? options.encounter?.start ?? null,
      parts: null,
      timerBase: null,
    };
    const blacklist = mode === 'blacklist';
    field('text-title').textContent = { add: 'Add event text', edit: 'Edit event text', blacklist: 'Blacklist event text' }[mode];
    field('text-intro').textContent = blacklist
      ? 'Blacklisted texts are never listed in any encounter. Click a highlighted part to switch it between fixed text and a variable.'
      : mode === 'edit'
        ? `An event text of ${options.record.name}. Click a highlighted part to switch it between fixed text and a variable.`
        : 'Adds the text to the encounter’s event texts. Click a highlighted part to switch it between fixed text and a variable.';
    field('text-encounter-row').hidden = mode !== 'add';
    field('text-encounter-hint').textContent = '';
    for (const id of ['text-description-row', 'text-timer-row']) field(id).hidden = blacklist;
    field('text-existing').textContent = '';
    field('text-save').textContent = { add: 'Add', edit: 'Save', blacklist: 'Blacklist' }[mode];

    const kind = field('text-kind');
    kind.value = group?.kind || options.entry?.kind || options.kind || 'emote';
    kind.disabled = !!group;

    let parts;
    if (group) {
      // Blacklisting usually means "any of these", so every possible variable starts as one.
      parts = group.parts.map((p) => (p.slot
        ? { slot: p.slot, variable: blacklist || p.variable, value: p.values[0], values: p.values }
        : { text: p.text }));
    } else {
      parts = partsFromInput(options.entry?.text || options.text || '').parts || [];
    }
    field('text-input').value = templateOf(parts);

    if (mode === 'add') {
      const matches = options.encounter ? matchRecords(options.encounter, options.summary) : null;
      fillEncounters(options.record || matches?.[0] || null, matches);
    }
    field('text-check').textContent = '';
    dialog.showModal();
    prefill();
    show(parts);
    field('text-input').focus();
  }

  field('text-input')?.addEventListener('input', () => {
    const parsed = partsFromInput(field('text-input').value.trim(), state.parts);
    show(parsed.parts || null, parsed.error);
  });
  // A typed text may match an entry the encounter has; check again once typing pauses on a change.
  field('text-input')?.addEventListener('change', () => {
    if (state?.mode === 'add' && !state.group) {
      prefill();
      updateSave();
    }
  });
  field('text-encounter')?.addEventListener('change', () => {
    prefill();
    updateSave();
  });
  for (const id of ['text-timer-on', 'text-timer-mean', 'text-timer-spread']) field(id)?.addEventListener('input', updateSave);

  // Replaces the selected words in the text with {player}.
  field('text-player')?.addEventListener('click', () => {
    const input = field('text-input');
    const { selectionStart: from, selectionEnd: to, value } = input;
    if (from == null || from === to) {
      field('text-error').textContent = 'Select a player’s name in the text first.';
      return;
    }
    input.value = `${value.slice(0, from)}{player}${value.slice(to)}`;
    input.focus();
    input.setSelectionRange(from, from + 8);
    input.dispatchEvent(new Event('input'));
  });

  // Handled on submit, not on "close" (see the update dialog in app.js).
  dialog?.querySelector('form').addEventListener('submit', (event) => {
    const current = state;
    if (event.submitter?.value !== 'save' || !current) {
      state = null;
      return;
    }
    const text = field('text-input').value.trim();
    const kind = field('text-kind').value;
    const timerOn = field('text-timer-on').checked;
    const timer = timerOn ? timerFromInputs() : null;
    if (parse(text).error || timer?.error) {
      event.preventDefault();
      return;
    }
    const record = current.mode === 'edit' ? current.record : chosenRecord();
    state = null;
    if (current.mode === 'blacklist') {
      deps.send({ type: 'blacklist-event-text', kind, text });
      return;
    }
    deps.send({
      type: 'add-event-text',
      zone: record.zone,
      name: record.name,
      entry: {
        kind,
        text,
        original: current.entry?.text ?? null,
        description: field('text-description').value.trim() || null,
        // Unticked keeps a recorded timer when adding, and removes it when editing.
        setTimer: timerOn || current.mode === 'edit',
        timer,
      },
    });
  });

  // ---- lists ----

  // One encounter's event texts, on the Encounters data tab.
  function recordSection(record) {
    const box = make('div', 'et-list');
    const head = make('h4', null, `Event texts (${(record.eventTexts || []).length})`);
    if (deps?.host) {
      const add = make('button', 'et-add', 'Add +');
      add.type = 'button';
      add.title = 'Add an event text to this encounter by hand';
      add.addEventListener('click', () => open({ mode: 'add', record }));
      head.appendChild(add);
    }
    box.appendChild(head);
    box.appendChild(entryTable(record.eventTexts || [], 'No event texts yet. Add them from a fight’s timeline, or by hand.', true, (entry) => [
      ['Edit', 'Change the text, description or timer', () => open({ mode: 'edit', record, entry })],
      ['Remove', `Remove from ${record.name}`, () => deps.send({ type: 'remove-event-text', zone: record.zone, name: record.name, text: entry.text })],
      ['Blacklist', 'Never list this text in any encounter', () => open({ mode: 'blacklist', kind: entry.kind, text: entry.text })],
    ]));
    return box;
  }

  function renderBlacklist(host) {
    const data = deps?.getData();
    host.replaceChildren();
    const head = make('div', 'data-head');
    head.appendChild(make('p', 'muted', 'Event texts listed here are left out of every encounter’s timeline and list. Use it for player-triggered or system text, like Heroic Surge.'));
    if (deps?.host) {
      const add = make('button', 'et-add', 'Add +');
      add.type = 'button';
      add.addEventListener('click', () => open({ mode: 'blacklist' }));
      head.appendChild(add);
    }
    host.appendChild(head);
    host.appendChild(entryTable(data?.blacklist || [], 'Nothing blacklisted.', false, (entry) => [
      ['Remove', 'List this text again', () => deps.send({ type: 'unblacklist-event-text', text: entry.text })],
    ]));
  }

  function entryTable(entries, empty, withDetails, actions) {
    if (!entries.length) return make('p', 'muted', empty);
    const table = make('table', 'et-table');
    if (withDetails) {
      const head = table.createTHead().insertRow();
      for (const [text, cls] of [['Kind'], ['Text'], ['Timer', 'num'], ['']]) head.appendChild(make('th', cls, text));
    }
    const body = table.createTBody();
    for (const entry of entries) {
      const row = body.insertRow();
      row.insertCell().appendChild(kindTag(entry.kind));
      const text = row.insertCell();
      text.className = 'et-text-cell';
      text.appendChild(render(entry.text));
      if (entry.description) text.appendChild(make('div', 'et-description', entry.description));
      if (withDetails) {
        const timer = row.insertCell();
        timer.className = 'num';
        if (entry.timer) {
          timer.textContent = formatTimer(entry.timer.mean, entry.timer.spread);
          timer.title = describeRecorded(entry.timer);
        }
      }
      const cell = row.insertCell();
      cell.className = 'et-actions';
      if (!deps?.host) continue;
      for (const [label, title, run] of actions(entry)) {
        const button = make('button', null, label);
        button.type = 'button';
        button.title = title;
        button.addEventListener('click', (event) => {
          event.stopPropagation();
          run();
        });
        cell.appendChild(button);
      }
    }
    return table;
  }

  // deps: { send(message), host: bool, getData() }
  function init(options) {
    deps = options;
  }

  return { KINDS, init, parse, toRegex, render, kindTag, formatTimer, matchRecords, recordedEntry, open, recordSection, renderBlacklist };
})();
