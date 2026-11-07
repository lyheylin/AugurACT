'use strict';

// Player deaths: an × in the player's lane at each death. Data: summary.deaths (PlayerDeath).
(() => {
  const { make, fillLines } = window.AugurUtil;

  const deaths = (s) => (s && s.deaths) || [];

  window.AugurTimeline.register({
    key: 'death',
    label: 'Death',
    swatch: 'death-mark',
    legend: [{ swatch: 'death-mark', label: 'Death' }],
    columns: [{ header: 'Deaths', value: (s) => deaths(s).length }],

    items: deaths,
    times: (s) => deaths(s).map((d) => d.at),
    // A victim missing from the player list still gets a lane.
    playerLanes: (s) => deaths(s).map((d) => d.name),

    // An × centred on the second of death, with a wider invisible target so it's easy to click.
    draw(s, api) {
      for (const death of deaths(s)) {
        const lane = api.playerLane(death.name);
        if (!lane) continue;
        const dx = api.x(death.at);
        const cy = lane.y + lane.height / 2;
        const r = 4.5;
        const group = api.el('g', { class: `death${api.isSelected(death) ? ' selected' : ''}` });
        api.el('circle', { class: 'hit', cx: dx, cy, r: 8 }, group);
        api.el('path', { class: 'mark', d: `M${dx - r} ${cy - r} L${dx + r} ${cy + r} M${dx + r} ${cy - r} L${dx - r} ${cy + r}` }, group);
        const how = death.killer ? `killed by ${death.killer}` : 'died from pain and suffering';
        api.el('title', {}, group).textContent = `${api.clock(death.at)} ${death.name} ${how}`;
        api.selectable(group, death);
      }
    },

    // Deaths per character, most first, like ACT's Deaths column.
    panel: {
      title: 'Deaths',
      render(body, s) {
        const list = deaths(s);
        const counts = make('dl', 'counts death-counts');
        counts.append(make('dt', null, 'Deaths'), make('dd', null, s ? String(list.length) : '…'));
        const byName = new Map();
        for (const d of list) {
          if (!byName.has(d.name)) byName.set(d.name, []);
          byName.get(d.name).push(d);
        }
        for (const [name, own] of [...byName].sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0]))) {
          const dt = make('dt', null, name);
          dt.title = `${name}: ${[...new Set(own.map((d) => d.killer || 'pain and suffering'))].join(', ')}`;
          counts.append(dt, make('dd', null, String(own.length)));
        }
        body.replaceChildren(counts);
        const painOnly = list.filter((d) => !d.killer).length;
        if (painOnly) {
          body.appendChild(make('p', 'muted hint', `${painOnly} of these only logged "died from pain and suffering", which ACT's own count leaves out.`));
        }
      },
    },

    showDetail(view, death, fight) {
      const at = window.AugurUtil.clock(death.at - fight.start);
      view.title.textContent = `${at} · ${death.name} died`;
      const own = deaths(fight.summary).filter((d) => d.name === death.name);
      const nth = own.length > 1 ? `; death ${own.indexOf(death) + 1} of ${own.length} for ${death.name}` : '';
      const how = death.killer ? `killed by ${death.killer}` : 'died from pain and suffering, with no killer named in the log';
      view.summary.textContent = `${how}${nth}.`;
      fillLines(view.lines, death.lines, () => 'kind-death', fight.start);
    },
  });
})();
