'use strict';

// Event texts: emotes, NPC speech and narration. A mark per line, in the speaker's lane (narration has its own
// lane at the top), shaped and coloured by kind. Lines that differ only in their variables are one group: a
// click selects the whole group. The list under the timeline adds a group to the encounters data or blacklists
// it (web/eventtexts/), makes an ACT trigger for it, or removes it from this fight's list and timeline only.
// Triggers recorded for a text show under its row. A group that repeats on a
// steady timer (EventTextTimer in Core) shows its interval. A line that names players drops a line from its mark
// to each of their lanes, ending in a dot. Data: summary.eventTexts (EventTextGroup).
(() => {
  const { make, fillLines, plural } = window.AugurUtil;
  const texts = window.AugurEventTexts;
  const NARRATION = 'Narration';

  const groups = (s) => (s && s.eventTexts) || [];

  // Groups removed with "Remove", per fight. Kept while the page is open, and across re-analysis; never saved.
  const removed = new Map(); // fight key -> Set of group keys
  const fightKey = (s) => `${s.zone}|${s.startEpoch}`;
  const groupKey = (g) => `${g.kind}\n${g.template}`;
  const removedOf = (s) => removed.get(fightKey(s)) || new Set();
  const shown = (s) => groups(s).filter((g) => !removedOf(s).has(groupKey(g)));

  // "3:40 ± 13 s" for a steady timer, else null.
  function every(group) {
    const t = group.timer;
    return t?.steady ? texts.formatTimer(t.mean, t.spread) : null;
  }

  // Values seen per kind of variable, for the detail and the list.
  function variables(group) {
    const out = [];
    for (const part of group.parts) {
      if (part.slot && part.variable) out.push(`{${part.slot}}: ${part.values.slice(0, 8).join('; ')}${part.values.length > 8 ? '; …' : ''}`);
    }
    return out;
  }

  function laneOf(api, occurrence, kind) {
    if (occurrence.player) return api.playerLane(occurrence.player);
    if (occurrence.npc) return api.npcLane(occurrence.npc);
    return kind === 'narration' ? api.topLane(NARRATION) : null;
  }

  window.AugurTimeline.register({
    key: 'eventText',
    label: 'Event text',
    swatch: 'et-swatch et-emote',
    legend: [
      { swatch: 'et-swatch et-emote', label: 'Emote' },
      { swatch: 'et-swatch et-say', label: 'NPC say' },
      { swatch: 'et-swatch et-narration', label: 'Narration' },
    ],

    items: groups,
    times: (s) => groups(s).flatMap((g) => g.occurrences.map((o) => o.at)),
    topLanes: (s) => (groups(s).some((g) => g.occurrences.some((o) => !o.npc && !o.player)) ? [NARRATION] : []),
    npcLanes: (s) => groups(s).flatMap((g) => g.occurrences.filter((o) => o.npc).map((o) => o.npc)),
    playerLanes: (s) => groups(s).flatMap((g) => g.occurrences.filter((o) => o.player).map((o) => o.player)),

    // Below the barrage bars in an NPC lane, centred in the others.
    draw(s, api) {
      const markY = (lane) => (lane.kind === 'npc' ? lane.y + lane.height - 7 : lane.y + lane.height / 2);

      // Lines to the players each event names, first, so every mark sits on top of them.
      for (const group of shown(s)) {
        const selected = api.isSelected(group);
        for (const occurrence of group.occurrences) {
          const lane = laneOf(api, occurrence, group.kind);
          if (!lane) continue;
          const cx = api.x(occurrence.at);
          for (const name of occurrence.players || []) {
            const target = api.playerLane(name);
            if (!target || target === lane) continue;
            const ty = target.y + target.height / 2;
            const node = api.el('g', { class: `et-ref et-${group.kind}${selected ? ' selected' : ''}` });
            api.el('line', { class: 'ref-line', x1: cx, x2: cx, y1: markY(lane), y2: ty }, node);
            api.el('circle', { class: 'ref-end', cx, cy: ty, r: 2.5 }, node);
            api.el('title', {}, node).textContent = `${api.clock(occurrence.at)} ${name}: ${occurrence.text}`;
            api.selectable(node, group);
          }
        }
      }

      for (const group of shown(s)) {
        const selected = api.isSelected(group);
        for (const occurrence of group.occurrences) {
          const lane = laneOf(api, occurrence, group.kind);
          if (!lane) continue;
          const cx = api.x(occurrence.at);
          const cy = markY(lane);
          const node = api.el('g', { class: `event-text et-${group.kind}${selected ? ' selected' : ''}` });
          api.el('circle', { class: 'hit', cx, cy, r: 7 }, node);
          const r = 3.5;
          if (group.kind === 'say') api.el('circle', { class: 'mark', cx, cy, r }, node);
          else if (group.kind === 'emote') api.el('path', { class: 'mark', d: `M${cx} ${cy - r - 1} L${cx + r + 1} ${cy} L${cx} ${cy + r + 1} L${cx - r - 1} ${cy} Z` }, node);
          else api.el('rect', { class: 'mark', x: cx - r, y: cy - r, width: r * 2, height: r * 2 }, node);
          api.el('title', {}, node).textContent = `${api.clock(occurrence.at)} ${occurrence.text}`;
          api.selectable(node, group);
        }
      }
    },

    // The full list, under the timeline: each group with its variables, how often it came, and what to do with it.
    section: {
      title: 'Event texts',
      render(body, s, ctx) {
        const list = groups(s);
        body.replaceChildren();
        if (!s) {
          body.appendChild(make('p', 'muted', 'Analyzing…'));
          return;
        }
        if (!list.length) {
          body.appendChild(make('p', 'muted', 'No emotes, NPC speech or narration in this fight.'));
          return;
        }
        const record = ctx.host ? texts.matchRecords(ctx.encounter, s)[0] : null;
        if (ctx.host) {
          body.appendChild(make('p', 'muted hint', record
            ? `"Add +" adds a text to ${record.name} in the encounters data.`
            : 'This fight’s encounter isn’t in the encounters data yet; "Add +" lets you choose one.'));
        }

        const table = make('table', 'et-table et-fight');
        const head = table.createTHead().insertRow();
        for (const [text, cls, title] of [['Kind'], ['Text'], ['Lines', 'num'], ['First', 'num'], ['Timer', 'num', 'Average time between events, for texts that repeat on a steady timer'], ['']]) {
          const th = head.appendChild(make('th', cls, text));
          if (title) th.title = title;
        }
        const rows = table.createTBody();
        const hidden = removedOf(s);
        for (const group of [...list].filter((g) => !hidden.has(groupKey(g))).sort((a, b) => a.occurrences[0].at - b.occurrences[0].at)) {
          const row = rows.insertRow();
          row.className = 'et-row';
          row.tabIndex = 0;
          row.insertCell().appendChild(texts.kindTag(group.kind));
          const cell = row.insertCell();
          cell.className = 'et-text-cell';
          cell.appendChild(texts.render(group.template));
          const vars = variables(group);
          if (vars.length) cell.appendChild(make('div', 'muted et-values', vars.join(' · ')));
          const entry = ctx.host ? texts.recordedEntry(record, group) : null;
          if (entry?.description) cell.appendChild(window.AugurEffectRefs.text(entry.description, record, 'et-description'));
          Object.assign(row.insertCell(), { className: 'num', textContent: String(group.occurrences.length) });
          Object.assign(row.insertCell(), { className: 'num', textContent: window.AugurUtil.clock(group.occurrences[0].at - ctx.start) });
          const timer = row.insertCell();
          timer.className = 'num';
          timer.textContent = every(group) || '';
          if (group.timer?.steady) timer.title = `${group.timer.events} events, ${group.timer.min}–${group.timer.max} s apart`;

          const actions = row.insertCell();
          actions.className = 'et-actions';
          if (ctx.host) {
            // Already recorded: the same dialog updates its description and timer.
            const add = make('button', entry ? 'et-add et-recorded' : 'et-add', entry ? 'Update' : 'Add +');
            add.type = 'button';
            add.title = entry
              ? `${record.name} lists this as: ${entry.text}. Click to update its description or timer.`
              : 'Add this text to the encounter in the encounters data';
            add.addEventListener('click', (event) => {
              event.stopPropagation();
              texts.open({ mode: 'add', group, encounter: ctx.encounter, summary: s });
            });
            actions.appendChild(add);
            const trigger = make('button', null, 'Trigger +');
            trigger.type = 'button';
            trigger.title = 'Make an ACT trigger for this text, saved with it in the encounters data';
            trigger.addEventListener('click', (event) => {
              event.stopPropagation();
              texts.openTrigger({ group, encounter: ctx.encounter, summary: s });
            });
            actions.appendChild(trigger);
          }
          const remove = make('button', null, 'Remove');
          remove.type = 'button';
          remove.title = 'Take this text off this fight\u2019s list and timeline, without blacklisting it';
          remove.addEventListener('click', (event) => {
            event.stopPropagation();
            if (!removed.has(fightKey(s))) removed.set(fightKey(s), new Set());
            removed.get(fightKey(s)).add(groupKey(group));
            ctx.refresh(group);
          });
          actions.appendChild(remove);
          if (ctx.host) {
            const ban = make('button', null, 'Blacklist');
            ban.type = 'button';
            ban.title = 'Never list this text in any encounter';
            ban.addEventListener('click', (event) => {
              event.stopPropagation();
              texts.open({ mode: 'blacklist', group });
            });
            actions.appendChild(ban);
          }

          const choose = () => ctx.select(group);
          row.addEventListener('click', choose);
          row.addEventListener('keydown', (event) => {
            if (event.key === 'Enter' || event.key === ' ') {
              event.preventDefault();
              choose();
            }
          });
          if (entry) texts.appendTriggers(rows, record, entry, row.cells.length);
        }
        body.appendChild(table);

        if (hidden.size) {
          const note = make('p', 'muted hint et-removed', `${plural(hidden.size, 'text')} removed from this fight\u2019s list. `);
          const restore = make('button', 'link', 'Show again');
          restore.type = 'button';
          restore.addEventListener('click', () => {
            removed.delete(fightKey(s));
            ctx.refresh();
          });
          note.appendChild(restore);
          body.appendChild(note);
        }
      },
    },

    showDetail(view, group, fight) {
      const first = group.occurrences[0].at;
      view.title.replaceChildren(texts.kindTag(group.kind), document.createTextNode(' '), texts.render(group.template));
      const vars = variables(group);
      const timer = every(group);
      const when = `${plural(group.occurrences.length, 'line')}, first at ${window.AugurUtil.clock(first - fight.start)}`
        + (timer ? `, every ${timer} (${group.timer.events} events, ${group.timer.min}–${group.timer.max} s apart)` : '');
      view.summary.textContent = vars.length ? `${when}. ${vars.join('; ')}.` : `${when}.`;
      fillLines(view.lines, group.occurrences, () => `kind-et-${group.kind}`, fight.start);
    },
  });
})();
