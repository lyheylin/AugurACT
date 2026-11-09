'use strict';

// Event texts on the page outside the timeline: each recorded encounter's list on the Encounters data tab, with
// each text's ACT triggers under it (eventtexts/trigger.js), and the Blacklist tab. Registered with AugurPage, which places them; the timeline part is mechanics/eventtext.js.
(() => {
  const { make, orderButtons, removeCell } = window.AugurUtil;
  const { render, kindTag, formatTimer, describeRecorded, open, openTrigger, appendTriggers } = window.AugurEventTexts;
  const ctx = () => window.AugurPage.ctx;

  // One encounter's event texts, on the Encounters data tab.
  function recordSection(record) {
    const box = make('div', 'et-list');
    const head = make('h4', null, `Event texts (${(record.eventTexts || []).length})`);
    if (ctx()?.host) {
      const add = make('button', 'et-add', 'Add +');
      add.type = 'button';
      add.title = 'Add an event text to this encounter by hand';
      add.addEventListener('click', () => open({ mode: 'add', record }));
      head.appendChild(add);
    }
    box.appendChild(head);
    box.appendChild(entryTable(record.eventTexts || [], {
      empty: 'No event texts yet. Add them from a fight’s timeline, or by hand.',
      withDetails: true,
      record,
      actions: (entry) => [
        ['Trigger +', 'Make an ACT trigger for this text', () => openTrigger({ record, entry })],
        ['Edit', 'Change the text, description or timer', () => open({ mode: 'edit', record, entry })],
        ['Blacklist', 'Never list this text in any encounter', () => open({ mode: 'blacklist', kind: entry.kind, text: entry.text })],
      ],
      remove: (entry) => [`Remove from ${record.name}`, () => ctx().send({ type: 'remove-event-text', zone: record.zone, name: record.name, text: entry.text })],
      children: (body, entry, columns) => appendTriggers(body, record, entry, columns),
      move: (entry, offset) => ctx().send({ type: 'move-event-text', zone: record.zone, name: record.name, text: entry.text, offset }),
    }));
    return box;
  }

  function renderBlacklist(host) {
    const data = ctx()?.getData();
    host.replaceChildren();
    const head = make('div', 'data-head');
    head.appendChild(make('p', 'muted', 'Event texts listed here are left out of every encounter’s timeline and list. Use it for player-triggered or system text, like Heroic Surge.'));
    if (ctx()?.host) {
      const add = make('button', 'et-add', 'Add +');
      add.type = 'button';
      add.addEventListener('click', () => open({ mode: 'blacklist' }));
      head.appendChild(add);
    }
    host.appendChild(head);
    host.appendChild(entryTable(data?.blacklist || [], {
      empty: 'Nothing blacklisted.',
      remove: (entry) => ['Take off the blacklist and list this text again', () => ctx().send({ type: 'unblacklist-event-text', text: entry.text })],
    }));
  }

  // actions(entry) gives its buttons, and remove(entry) its remove cross as [title, run], last in the row.
  // children(body, entry, columns), when given, adds rows under each entry's own, each ending in a remove cell.
  // record is the encounter the entries belong to, where their descriptions' {effect} references are looked up
  // first. move(entry, offset), when given, adds the up / down arrows that order the list.
  function entryTable(entries, { empty, withDetails = false, actions, remove, children, record = null, move = null }) {
    if (!entries.length) return make('p', 'muted', empty);
    const table = make('table', 'et-table');
    const ordered = move && ctx()?.host;
    if (withDetails) {
      const head = table.createTHead().insertRow();
      for (const [text, cls] of [['Kind'], ['Text'], ['Timer', 'num'], [''], ...(ordered ? [['']] : []), ['']]) head.appendChild(make('th', cls, text));
    }
    const body = table.createTBody();
    for (const [index, entry] of entries.entries()) {
      const row = body.insertRow();
      row.insertCell().appendChild(kindTag(entry.kind));
      const text = row.insertCell();
      text.className = 'et-text-cell';
      text.appendChild(render(entry.text));
      if (entry.description) text.appendChild(window.AugurEffectRefs.text(entry.description, record, 'et-description'));
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
      // Rows under the entry (its triggers) line up with its own, and leave the arrows' column empty.
      const before = body.rows.length;
      children?.(body, entry, row.cells.length);
      if (ordered) {
        const order = row.insertCell();
        order.className = 'order-cell';
        order.appendChild(orderButtons(index, entries.length, (offset) => move(entry, offset), 'this text'));
        for (const child of [...body.rows].slice(before)) child.insertCell(child.cells.length - 1);
      }
      const [removeTitle, removeRun] = (ctx()?.host && remove?.(entry)) || [];
      removeCell(row, removeTitle, removeRun);
      if (!ctx()?.host) continue;
      for (const [label, title, run] of actions?.(entry) || []) {
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

  window.AugurPage.register({
    key: 'eventTexts',
    recordSection,
    tabs: [{ key: 'blacklist', label: 'Blacklist', render: renderBlacklist }],
  });
})();
