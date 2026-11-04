'use strict';

// Barrage and bulwark: a bar per cast in its NPC's lane, from the warning to the release, coloured by outcome,
// with the logger's group's bulwark presses as ticks beneath it. Data: summary.barrages (BarrageCast).
(() => {
  const { make, damage, fmtSeconds, median, plural, escapeRegex, fillLines } = window.AugurUtil;

  const casts = (s) => (s && s.barrages) || [];
  const count = (list, outcome) => list.filter((c) => c.outcome === outcome).length;
  const outcomeName = (cast) => (cast.outcome === 'Unresolved' ? 'No release' : cast.outcome);

  // Seconds between casts of one NPC instance; longer gaps are separate pulls.
  function intervals(list) {
    const byId = new Map();
    for (const c of list) {
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

  window.AugurTimeline.register({
    key: 'barrage',
    label: 'Barrage',
    swatch: 'sw blocked',
    legend: [
      { swatch: 'sw blocked', label: 'Blocked' },
      { swatch: 'sw missed', label: 'Missed' },
      { swatch: 'sw cancelled', label: 'Cancelled' },
      { swatch: 'sw unresolved', label: 'No release' },
      { swatch: 'tick', label: 'Bulwark' },
      { swatch: 'tick late', label: 'Bulwark after release' },
    ],
    columns: [
      { header: 'Barrages', value: (s) => casts(s).length },
      { header: 'Blocked', value: (s) => count(casts(s), 'Blocked') },
      { header: 'Missed', value: (s) => count(casts(s), 'Missed'), className: 'missed-count' },
      { header: 'No release', value: (s) => count(casts(s), 'Unresolved') },
    ],
    // On the Encounters data tab: a yes/no per NPC from EncounterStore's NpcRecord.
    dataFlag: { header: 'Barrage', field: 'releasesBarrage', describe: (name) => `${name} releases barrages` },

    items: casts,
    times: (s) => casts(s).flatMap((c) => [c.readiedAt, c.resolvedAt ?? c.readiedAt]),
    npcLanes: (s) => casts(s).map((c) => c.npc),

    draw(s, api) {
      for (const cast of casts(s)) {
        const lane = api.npcLane(cast.npc);
        if (!lane) continue;
        const { y } = lane;
        const x1 = api.x(cast.readiedAt);
        const end = cast.resolvedAt ?? cast.readiedAt + 10;
        const x2 = Math.max(x1 + 3, api.x(end));
        const bar = api.el('rect', {
          class: `cast ${cast.outcome.toLowerCase()}${api.isSelected(cast) ? ' selected' : ''}`,
          x: x1, y: y + 6, width: x2 - x1, height: 14, rx: 2,
        });
        const target = cast.target ? ` at ${cast.target}` : '';
        api.el('title', {}, bar).textContent = `${api.clock(cast.readiedAt)} ${cast.npc}${target}: ${outcomeName(cast)}`;
        api.selectable(bar, cast);

        if (cast.outcome !== 'Cancelled' && cast.resolvedAt != null) {
          const rx = api.x(end);
          api.el('path', { class: 'release', d: `M${rx} ${y + 3} l4 4 l-4 4 l-4 -4 z` });
        }

        for (const second of new Set((cast.bulwarks || []).map((b) => b.at))) {
          const late = cast.resolvedAt != null && second > cast.resolvedAt;
          const bx = api.x(second);
          api.el('line', { class: `bulwark${late ? ' late' : ''}`, x1: bx, x2: bx, y1: y + 22, y2: y + 31 });
        }
      }
    },

    panel: {
      title: 'Barrages',
      // s is null while the encounter is still being analyzed.
      render(body, s) {
        const list = casts(s);
        const counts = make('dl', 'counts');
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
          counts.append(dt, make('dd', null, s ? String(value) : '…'));
        }

        const stats = make('div', 'npc-stats');
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
          const rows = [
            ['Type', own.some((c) => c.target) ? 'Targeted (raid)' : 'Untargeted (group)'],
            ['Warning', fmtSeconds(median(released.filter((c) => c.warningSeen !== false).map((c) => c.resolvedAt - c.readiedAt)))],
            ['Every', fmtSeconds(median(intervals(own)))],
          ];
          for (const [k, v] of rows) dl.append(make('dt', null, k), make('dd', null, v));
          card.append(title, rate, dl);
          stats.appendChild(card);
        }
        body.replaceChildren(counts, stats);
      },
    },

    // view: { title, summary, lines } elements of the detail box under the timeline.
    showDetail(view, cast, fight) {
      const at = window.AugurUtil.clock(cast.readiedAt - fight.start);
      view.title.textContent = `${at} · ${cast.npc}${cast.target ? ` → ${cast.target}` : ''} · ${outcomeName(cast)}`;

      const parts = [];
      if (cast.warningSeen === false) parts.push('released with no warning emote in the log');
      else if (cast.resolvedAt != null && cast.outcome !== 'Cancelled') parts.push(`released ${cast.resolvedAt - cast.readiedAt} s after the warning`);
      else if (cast.outcome === 'Unresolved') parts.push('never released or cancelled');
      const onTime = (cast.bulwarks || []).filter((b) => cast.resolvedAt == null || b.at <= cast.resolvedAt);
      const late = (cast.bulwarks || []).filter((b) => cast.resolvedAt != null && b.at > cast.resolvedAt);
      if (onTime.length) parts.push(`${plural(onTime.length, 'bulwark')} in your group, last ${cast.resolvedAt - onTime[onTime.length - 1].at} s before release`);
      else if (cast.outcome === 'Blocked' || cast.outcome === 'Missed') parts.push('no bulwark seen in your group before release');
      if (late.length) parts.push(`${plural(late.length, 'bulwark')} pressed ${late[0].at - cast.resolvedAt} s too late`);
      if (cast.splashHits) {
        const amount = cast.splashDamage > 0 ? ` for ${damage(cast.splashDamage)}` : ', though it did no damage';
        parts.push(`${cast.target ? 'splash' : 'barrage'} hit ${plural(cast.splashPlayers, 'player')}${amount}`);
      }
      if (cast.targetHits) parts.push(`target took ${damage(cast.targetDamage)}`);
      view.summary.textContent = `${parts.join('; ')}.`;

      const target = (cast.target || '').toLowerCase();
      fillLines(view.lines, cast.lines, (text) => {
        if (/releases a barrage|readies a barrage|prepares to unleash|cancels the barrage/.test(text)) return 'kind-release';
        if (/applies a bulwark/.test(text)) return 'kind-bulwark';
        if (/Barrage/.test(text)) {
          const onTarget = target && new RegExp(` hits ${escapeRegex(target)} `, 'i').test(text);
          return `kind-hit${onTarget ? ' on-target' : ''}`;
        }
        return null;
      });
    },
  });
})();
