'use strict';

// The dialog that adds or edits an encounter's event text (with its description and timer), or blacklists one.
// Built on first use, so the page's HTML doesn't carry it. Opened by the timeline's list (mechanics/eventtext.js)
// and the lists in eventtexts/feature.js, through AugurEventTexts.open(options).
(() => {
  const { make, plural } = window.AugurUtil;
  const {
    SLOTS, CANDIDATE, escapeLiteral, parse, toRegex, formatTimer, describeRecorded, readSeconds, secondsText,
    mergeTimer, matchRecords, recordedEntry,
  } = window.AugurEventTexts;
  const ctx = () => window.AugurPage.ctx;

  // Static markup only: log text is never written into it as HTML.
  const MARKUP = `
<dialog id="text-dialog" aria-labelledby="text-title">
  <form method="dialog">
    <h2 id="text-title">Add event text</h2>
    <p id="text-intro" class="muted"></p>
    <div class="text-row">
      <label class="text-field" id="text-encounter-row"><span>Encounter</span><select id="text-encounter"></select></label>
      <label class="text-field"><span>Kind</span>
        <select id="text-kind">
          <option value="emote">Emote</option>
          <option value="say">NPC say</option>
          <option value="narration">Narration</option>
        </select>
      </label>
    </div>
    <p id="text-encounter-hint" class="muted hint"></p>
    <label class="text-field"><span>Text</span><input id="text-input" type="text" spellcheck="false" autocomplete="off"></label>
    <div class="text-row">
      <p class="muted hint">Variables: {player} {class} {archetype} {number}. Write a literal brace as {{ or }}.</p>
      <button type="button" id="text-player" title="Select a name in the text, then click this">Selection is a player name</button>
    </div>
    <div id="text-chips" class="et-chips" aria-label="Parts of the text; click one to switch it between fixed text and a variable"></div>
    <div id="text-seen"></div>
    <p id="text-check" role="status"></p>
    <p id="text-existing" class="muted hint"></p>
    <label class="text-field" id="text-description-row"><span>Description</span>
      <textarea id="text-description" rows="2" placeholder="Your note: what it means, what to do"></textarea>
    </label>
    <div class="text-field" id="text-timer-row">
      <label class="et-timer-toggle"><input type="checkbox" id="text-timer-on"> <span id="text-timer-label">Attach timer</span></label>
      <div id="text-timer-values" class="et-timer-values">
        <span>every</span>
        <input id="text-timer-mean" type="text" inputmode="decimal" size="7" aria-label="Seconds between events, or m:ss" placeholder="3:40">
        <span>±</span>
        <input id="text-timer-spread" type="text" inputmode="decimal" size="4" aria-label="Plus or minus seconds" placeholder="13">
        <span>s</span>
      </div>
      <p id="text-timer-note" class="muted hint"></p>
    </div>
    <p id="text-error" role="alert"></p>
    <div class="dialog-buttons">
      <button type="submit" value="cancel" formnovalidate>Cancel</button>
      <button type="submit" value="save" id="text-save" class="primary">Add</button>
    </div>
  </form>
</dialog>`;

  let dialog = null;
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
    return (ctx().getData()?.encounters || []).find((r) => r.zone === zone && r.name === name) || null;
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
    const records = ctx().getData()?.encounters || [];
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
    if (!ctx()?.host) return;
    build();
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


  function build() {
    if (dialog) return;
    const holder = document.createElement('div');
    holder.innerHTML = MARKUP;
    dialog = holder.firstElementChild;
    document.body.appendChild(dialog);

    field('text-input').addEventListener('input', () => {
      const parsed = partsFromInput(field('text-input').value.trim(), state.parts);
      show(parsed.parts || null, parsed.error);
    });
    // A typed text may match an entry the encounter has; check again once typing pauses on a change.
    field('text-input').addEventListener('change', () => {
      if (state?.mode === 'add' && !state.group) {
        prefill();
        updateSave();
      }
    });
    field('text-encounter').addEventListener('change', () => {
      prefill();
      updateSave();
    });
    for (const id of ['text-timer-on', 'text-timer-mean', 'text-timer-spread']) field(id).addEventListener('input', updateSave);

    // Replaces the selected words in the text with {player}.
    field('text-player').addEventListener('click', () => {
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
    dialog.querySelector('form').addEventListener('submit', (event) => {
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
        ctx().send({ type: 'blacklist-event-text', kind, text });
        return;
      }
      ctx().send({
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
  }

  window.AugurEventTexts.open = open;
})();
