'use strict';

// ACT triggers for event texts: the dialog that makes one, and the rows that show an event text's triggers under
// it, with Edit (the same dialog, filled in), Copy (ACT's XML on the clipboard), Import (straight into ACT's custom
// triggers) and Remove. Triggers are
// saved under the text in the encounters data (EventTrigger in Core); the regex starts as the one Core built from
// the text's log line (TriggerRegex), which matches the line as the game wrote it, markup included.
//
// A {player} group can be swapped to ACT's YOU group: ACT then fires the trigger only when that group holds the
// character name of whoever is logging, in any case (ACT 3.2.1.251; FormActMain.ParseCustomSingle). The literal
// word YOU in a line doesn't count.
//
// Opened from the list under a fight's timeline (mechanics/eventtext.js) and each encounter's list
// (eventtexts/feature.js), through AugurEventTexts.openTrigger(options).
(() => {
  const { make, plural } = window.AugurUtil;
  const { render, kindTag, matchRecords, recordedEntry, encounterOptions, recordOf } = window.AugurEventTexts;
  const ctx = () => window.AugurPage.ctx;

  // ACT's sound types (EventTrigger in Core).
  const SOUND_NONE = 0;
  const SOUND_TTS = 3;

  // Static markup only: log text is never written into it as HTML.
  const MARKUP = `
<dialog id="trigger-dialog" aria-labelledby="trigger-title">
  <form method="dialog">
    <h2 id="trigger-title">Add trigger</h2>
    <p id="trigger-text" class="trigger-text"></p>
    <label class="text-field" id="trigger-encounter-row"><span>Encounter</span><select id="trigger-encounter"></select></label>
    <p id="trigger-encounter-hint" class="muted hint"></p>
    <label class="text-field"><span>Regular expression</span>
      <input id="trigger-regex" type="text" spellcheck="false" autocomplete="off">
    </label>
    <p id="trigger-check" class="hint" role="status"></p>
    <div id="trigger-you" class="trigger-you">
      <span class="trigger-you-title">Player groups</span>
      <div id="trigger-you-groups" class="trigger-you-groups"></div>
      <div class="trigger-you-row">
        <button type="button" id="trigger-you-swap" disabled>Swap to YOU</button>
        <span class="muted hint">A YOU group only matches when it holds your character’s name, so the trigger fires only for you. Lines that write YOU instead of your name don’t match.</span>
      </div>
    </div>
    <div class="text-field">
      <span>Audio alert</span>
      <div class="trigger-sound">
        <label><input type="radio" name="trigger-sound" value="0" checked> None</label>
        <label><input type="radio" name="trigger-sound" value="3"> Text to speech</label>
        <input id="trigger-tts" type="text" aria-label="What text to speech says" spellcheck="false" autocomplete="off">
      </div>
      <p id="trigger-groups" class="muted hint"></p>
    </div>
    <div class="trigger-row">
      <label class="text-field"><span>Category / zone</span><input id="trigger-category" type="text" spellcheck="false" autocomplete="off"></label>
      <button type="button" id="trigger-zone">Add zone</button>
    </div>
    <label class="trigger-check"><input type="checkbox" id="trigger-restrict"> Restrict to category zone</label>
    <label class="text-field"><span>Timer or tab name</span><input id="trigger-timer-name" type="text" spellcheck="false" autocomplete="off"></label>
    <div class="trigger-checks">
      <label class="trigger-check"><input type="checkbox" id="trigger-timer"> Trigger timer</label>
      <label class="trigger-check"><input type="checkbox" id="trigger-tab"> Add results tab</label>
    </div>
    <p id="trigger-error" role="alert"></p>
    <div class="dialog-buttons">
      <button type="submit" value="cancel" formnovalidate>Cancel</button>
      <button type="submit" value="save" id="trigger-save" class="primary">Add trigger</button>
    </div>
  </form>
</dialog>`;

  let dialog = null;
  const field = (id) => document.getElementById(id);
  // { record, entry, group, trigger (the saved one being edited), zone, defaultRegex }
  let state = null;

  const sound = () => Number(dialog.querySelector('input[name="trigger-sound"]:checked').value);

  // The text the trigger is for, and where its regex starts, for the chosen encounter.
  function target() {
    const record = state.group ? recordOf(field('trigger-encounter').value) : state.record;
    const entry = state.group ? recordedEntry(record, state.group) : state.entry;
    const group = state.group;
    return {
      record,
      entry,
      kind: entry?.kind ?? group.kind,
      text: entry?.text ?? group.template,
      // A recorded regex guessed without a line loses to the one from this fight's lines.
      regex: entry?.regex && (entry.regexFromLine || !group) ? entry.regex : group?.regex ?? entry?.regex ?? '',
      fromLine: group ? true : !!entry?.regexFromLine,
      // Log lines to test the regex on: the fight's, or the one saved with the text.
      lines: group ? group.occurrences.map((o) => o.raw) : entry?.line ? [entry.line] : [],
    };
  }

  // Named groups in the regex, which text to speech can say as ${name}. Repeats (two YOU groups) once.
  function groupNames(pattern) {
    return [...new Set([...pattern.matchAll(/\(\?<([A-Za-z][A-Za-z0-9]*)>/g)].map((m) => m[1]))];
  }

  // The groups that can be swapped: (?<player>, or (?<player1>, (?<player2>, ... and (?<YOU>, with where each
  // starts.
  const PLAYER_GROUP = /\(\?<(player\d*|YOU)>/g;
  function playerGroups(pattern) {
    return [...pattern.matchAll(PLAYER_GROUP)].map((m) => ({ name: m[1], index: m.index, length: m[0].length }));
  }

  // The regex as JavaScript can run it: player and YOU groups get a name per position, since JavaScript
  // doesn't allow two groups of one name. Null when it still can't compile.
  function testRegex(pattern) {
    let k = 0;
    try {
      return new RegExp(pattern.replace(PLAYER_GROUP, () => `(?<pg${k++}>`));
    } catch {
      return null;
    }
  }

  // The player groups as checkboxes, each by its name.
  function showPlayerGroups(pattern) {
    const groups = playerGroups(pattern);
    const box = field('trigger-you');
    box.hidden = !groups.length;
    const list = field('trigger-you-groups');
    if (state.youChecked.size && [...state.youChecked].some((i) => i >= groups.length)) state.youChecked.clear();
    list.replaceChildren();
    groups.forEach((g, k) => {
      const label = make('label', `trigger-check trigger-you-group${g.name === 'YOU' ? ' is-you' : ''}`);
      const check = document.createElement('input');
      check.type = 'checkbox';
      check.checked = state.youChecked.has(k);
      check.addEventListener('change', () => {
        if (check.checked) state.youChecked.add(k);
        else state.youChecked.delete(k);
        swapLabel(groups);
      });
      label.append(check, make('code', null, g.name));
      list.appendChild(label);
    });
    swapLabel(groups);
  }

  function swapLabel(groups) {
    const chosen = [...state.youChecked].map((k) => groups[k]).filter(Boolean);
    const button = field('trigger-you-swap');
    button.disabled = !chosen.length;
    button.textContent = chosen.length && chosen.every((g) => g.name === 'YOU') ? 'Swap back to player' : 'Swap to YOU';
  }

  // Swaps the checked groups: player groups become YOU; or, when only YOU groups are checked, they become player
  // groups again under a free name: player when it's the only one, else the first free of player1, player2, ...
  // Text to speech follows the rename.
  function swapGroups() {
    const input = field('trigger-regex');
    const pattern = input.value;
    const groups = playerGroups(pattern);
    const chosen = [...state.youChecked].map((k) => groups[k]).filter(Boolean);
    const back = chosen.every((g) => g.name === 'YOU');
    const taken = new Set(groups.map((g) => g.name));
    const others = groups.filter((g) => g.name !== 'YOU').length;
    const single = back && others === 0 && chosen.length === 1;
    const freeName = () => {
      if (single) return 'player';
      let n = 1;
      while (taken.has(`player${n}`)) n++;
      taken.add(`player${n}`);
      return `player${n}`;
    };
    let result = pattern;
    const tts = field('trigger-tts');
    // From the end, so earlier positions stay valid.
    for (const g of [...chosen].sort((a, b) => b.index - a.index)) {
      if (back !== (g.name === 'YOU')) continue;
      const name = back ? freeName() : 'YOU';
      result = `${result.slice(0, g.index)}(?<${name}>${result.slice(g.index + g.length)}`;
      tts.value = tts.value.split(`\${${g.name}}`).join(`\${${name}}`);
    }
    input.value = result;
    update();
  }

  function update() {
    const t = target();
    const pattern = field('trigger-regex').value;
    const check = field('trigger-check');
    check.classList.remove('error');
    const rule = pattern ? testRegex(pattern) ?? undefined : null;
    if (!pattern) {
      check.textContent = '';
    } else if (rule === undefined) {
      check.textContent = 'This regex can’t be tested in the page; ACT may still accept it.';
    } else if (t.lines.length) {
      const n = t.lines.filter((line) => rule.test(line)).length;
      check.textContent = state.group
        ? `Matches ${n} of ${plural(t.lines.length, 'line')} in this fight, as ACT sees them.`
        : n ? 'Matches the log line saved with this text.' : 'Doesn’t match the log line saved with this text.';
      check.classList.toggle('error', n === 0);
    } else {
      check.textContent = 'No log line is saved with this text, so the regex was built from the text alone. Check it in ACT, or add the text again from a fight.';
    }

    showPlayerGroups(pattern);
    const names = groupNames(pattern);
    field('trigger-groups').textContent = names.length
      ? `Text to speech can say what a group matched: ${names.map((n) => `\${${n}}`).join(' ')}.`
      : '';
    const tts = sound() === SOUND_TTS;
    field('trigger-tts').disabled = !tts;
    field('trigger-zone').disabled = !state.zone();

    let problem = '';
    if (state.group && !t.record) problem = 'Choose the encounter to save the trigger in.';
    else if (!pattern.trim()) problem = 'Write the regular expression.';
    else if (tts && !field('trigger-tts').value.trim()) problem = 'Write what text to speech should say.';
    else if (field('trigger-timer').checked && !field('trigger-timer-name').value.trim()) problem = 'A trigger timer needs the name of the spell timer to start.';
    field('trigger-error').textContent = problem;
    field('trigger-save').disabled = !!problem;
  }

  // Shows the text the chosen encounter would save it under, and resets the regex unless the user changed it.
  function retarget() {
    const t = target();
    const text = field('trigger-text');
    text.replaceChildren(kindTag(t.kind), document.createTextNode(' '), render(t.text));
    const regex = field('trigger-regex');
    if (regex.value === state.defaultRegex) regex.value = t.regex;
    state.defaultRegex = t.regex;
    field('trigger-encounter-hint').textContent = state.group && t.record && !t.entry
      ? `${t.record.name} doesn’t list this text yet; saving adds it.`
      : '';
  }

  // options: { record, entry } from an encounter's list, or { group, encounter, summary } from a fight's; with
  // trigger too, the dialog edits that saved trigger of the entry instead of adding one.
  function open(options) {
    if (!ctx()?.host) return;
    build();
    const { group } = options;
    const editing = !group && options.trigger ? options.trigger : null;
    state = { record: options.record || null, entry: options.entry || null, group: group || null, trigger: editing, defaultRegex: '', youChecked: new Set() };
    field('trigger-title').textContent = editing ? 'Edit trigger' : 'Add trigger';
    field('trigger-save').textContent = editing ? 'Save trigger' : 'Add trigger';
    // "Add zone" fills in the encounter's zone: the chosen record's, or the ACT fight's.
    state.zone = () => {
      const z = (group ? recordOf(field('trigger-encounter').value)?.zone : state.record?.zone) || options.encounter?.zone || '';
      return z === '(unknown zone)' ? '' : z;
    };

    field('trigger-encounter-row').hidden = !group;
    field('trigger-encounter-hint').textContent = '';
    if (group) {
      const matches = matchRecords(options.encounter, options.summary);
      const count = encounterOptions(field('trigger-encounter'), matches[0] || null, matches[0]?.zone || options.encounter?.zone);
      if (!count) field('trigger-encounter-hint').textContent = 'No encounters recorded yet. Use "Update encounters data" on this fight first.';
    }
    field('trigger-regex').value = '';
    dialog.querySelector('input[name="trigger-sound"][value="0"]').checked = true;
    for (const id of ['trigger-tts', 'trigger-category', 'trigger-timer-name']) field(id).value = '';
    for (const id of ['trigger-restrict', 'trigger-timer', 'trigger-tab']) field(id).checked = false;
    retarget();
    if (editing) {
      field('trigger-regex').value = editing.regex || '';
      const soundValue = editing.sound === SOUND_TTS ? SOUND_TTS : SOUND_NONE;
      dialog.querySelector(`input[name="trigger-sound"][value="${soundValue}"]`).checked = true;
      field('trigger-tts').value = editing.soundData || '';
      field('trigger-category').value = editing.category || '';
      field('trigger-timer-name').value = editing.timerName || '';
      field('trigger-restrict').checked = !!editing.restrictToZone;
      field('trigger-timer').checked = !!editing.timer;
      field('trigger-tab').checked = !!editing.resultsTab;
    }
    dialog.showModal();
    update();
    field('trigger-regex').focus();
  }

  function build() {
    if (dialog) return;
    const holder = document.createElement('div');
    holder.innerHTML = MARKUP;
    dialog = holder.firstElementChild;
    document.body.appendChild(dialog);
    field('trigger-tts').placeholder = 'What to say, like: Spread from ${player}';

    for (const node of dialog.querySelectorAll('input')) node.addEventListener('input', update);
    field('trigger-encounter').addEventListener('change', () => {
      retarget();
      update();
    });
    field('trigger-you-swap').addEventListener('click', () => {
      swapGroups();
      state.youChecked.clear();
      update();
    });
    field('trigger-zone').addEventListener('click', () => {
      field('trigger-category').value = state.zone();
      update();
    });

    // Handled on submit, not on "close" (see the update dialog in app.js).
    dialog.querySelector('form').addEventListener('submit', (event) => {
      const current = state;
      if (event.submitter?.value !== 'save' || !current) {
        state = null;
        return;
      }
      update();
      if (field('trigger-save').disabled) {
        event.preventDefault();
        return;
      }
      const t = target();
      state = null;
      const where = current.trigger
        ? { type: 'edit-event-trigger', text: t.text, original: current.trigger }
        : { type: 'add-event-trigger', entry: { kind: t.kind, text: t.text, line: current.group?.occurrences[0]?.raw ?? null } };
      ctx().send({
        ...where,
        zone: t.record.zone,
        name: t.record.name,
        trigger: {
          regex: field('trigger-regex').value,
          sound: sound(),
          soundData: sound() === SOUND_NONE ? null : field('trigger-tts').value.trim(),
          category: field('trigger-category').value.trim() || null,
          restrictToZone: field('trigger-restrict').checked,
          timer: field('trigger-timer').checked,
          timerName: field('trigger-timer-name').value.trim() || null,
          resultsTab: field('trigger-tab').checked,
        },
      });
    });
  }

  // ---- an event text's triggers, as rows under its own in a list ----

  function describe(trigger) {
    const parts = [];
    if (trigger.sound === SOUND_TTS) parts.push(`TTS “${trigger.soundData || ''}”`);
    if (trigger.category) parts.push(trigger.restrictToZone ? `${trigger.category} (only in this zone)` : trigger.category);
    if (trigger.timer) parts.push(`timer ${trigger.timerName || ''}`);
    if (trigger.resultsTab) parts.push(trigger.timerName ? `results tab ${trigger.timerName}` : 'results tab');
    return parts.join(' · ') || 'No alert';
  }

  // Adds a row per trigger of the entry (of record) to body, after the entry's own row. columns is the table's
  // column count: a tag under the first, the trigger across the middle, and buttons in the last.
  function appendTriggers(body, record, entry, columns) {
    for (const trigger of entry?.triggers || []) {
      const row = body.insertRow();
      row.className = 'et-trigger-row';
      row.insertCell().appendChild(make('span', 'et-trigger-tag', 'Trigger'));
      const cell = row.insertCell();
      cell.colSpan = Math.max(1, columns - 2);
      cell.className = 'et-trigger-cell';
      cell.append(make('code', 'et-regex', trigger.regex), make('div', 'muted et-trigger-info', describe(trigger)));
      const actions = row.insertCell();
      actions.className = 'et-actions';
      if (!ctx()?.host) continue;
      const edit = make('button', null, 'Edit');
      edit.type = 'button';
      edit.title = 'Change the trigger’s regex, alert and settings';
      edit.addEventListener('click', (event) => {
        event.stopPropagation();
        open({ record, entry, trigger });
      });
      actions.appendChild(edit);
      for (const [label, title, message] of [
        ['Copy', 'Copy the trigger as ACT’s XML, to paste into ACT or share', { type: 'copy-trigger', trigger }],
        ['Import', 'Add the trigger to ACT’s custom triggers', { type: 'import-trigger', trigger }],
        ['Remove', `Remove the trigger from ${record.name}`, { type: 'remove-event-trigger', zone: record.zone, name: record.name, text: entry.text, trigger }],
      ]) {
        const button = make('button', null, label);
        button.type = 'button';
        button.title = title;
        button.addEventListener('click', (event) => {
          event.stopPropagation();
          ctx().send(message);
        });
        actions.appendChild(button);
      }
    }
  }

  window.AugurEventTexts.openTrigger = open;
  window.AugurEventTexts.appendTriggers = appendTriggers;
})();
