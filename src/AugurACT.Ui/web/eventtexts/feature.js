'use strict';

// Event texts on the page outside the timeline: each recorded encounter's list on the Encounters data tab, with
// each text's ACT triggers under it (eventtexts/trigger.js), and the Blacklist tab. Registered with AugurPage, which places them; the timeline part is mechanics/eventtext.js.
(() => {
  const { make } = window.AugurUtil;
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
    box.appendChild(entryTable(record.eventTexts || [], 'No event texts yet. Add them from a fight’s timeline, or by hand.', true, (entry) => [
      ['Trigger +', 'Make an ACT trigger for this text', () => openTrigger({ record, entry })],
      ['Edit', 'Change the text, description or timer', () => open({ mode: 'edit', record, entry })],
      ['Remove', `Remove from ${record.name}`, () => ctx().send({ type: 'remove-event-text', zone: record.zone, name: record.name, text: entry.text })],
      ['Blacklist', 'Never list this text in any encounter', () => open({ mode: 'blacklist', kind: entry.kind, text: entry.text })],
    ], (body, entry, columns) => appendTriggers(body, record, entry, columns)));
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
    host.appendChild(entryTable(data?.blacklist || [], 'Nothing blacklisted.', false, (entry) => [
      ['Remove', 'List this text again', () => ctx().send({ type: 'unblacklist-event-text', text: entry.text })],
    ]));
  }

  // children(body, entry, columns), when given, adds rows under each entry's own.
  function entryTable(entries, empty, withDetails, actions, children) {
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
      children?.(body, entry, row.cells.length);
      if (!ctx()?.host) continue;
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

  window.AugurPage.register({
    key: 'eventTexts',
    recordSection,
    tabs: [{ key: 'blacklist', label: 'Blacklist', render: renderBlacklist }],
  });
})();
