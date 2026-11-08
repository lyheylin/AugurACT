'use strict';

// The timeline engine and the list of mechanics. The engine knows nothing about any one mechanic: it draws
// the time axis, any shared lanes a mechanic asks for above the rest (like "Narration"), a lane per NPC that
// some mechanic names, and a lane per player in the encounter (always, so
// the participants are visible even when nothing happened to them), then lets each mechanic that's switched
// on draw its own marks into those lanes. See docs/ADDING-A-MECHANIC.md for what a mechanic provides.
//
// Written with DOM calls only, so log text never becomes markup.
window.AugurTimeline = (() => {
  const SVG = 'http://www.w3.org/2000/svg';
  const LABEL_WIDTH = 190;
  const TOP_LANE_HEIGHT = 22;
  const NPC_LANE_HEIGHT = 34;
  const PLAYER_LANE_HEIGHT = 20;
  const AXIS_HEIGHT = 22;
  const PAD = 12;
  const { clock } = window.AugurUtil;
  const mechanics = [];
  let instances = 0;

  // Called once by each file in web/mechanics/; the order of registration is the order on the page.
  function register(mechanic) {
    mechanics.push(mechanic);
  }

  function el(name, attrs, parent) {
    const node = document.createElementNS(SVG, name);
    for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
    if (parent) parent.appendChild(node);
    return node;
  }

  function niceStep(span, width) {
    const target = span / Math.max(2, width / 90);
    for (const step of [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600]) {
      if (step >= target) return step;
    }
    return 7200;
  }

  function lanesOf(summary) {
    const tops = [...new Set(mechanics.flatMap((m) => (m.topLanes ? m.topLanes(summary) : [])))];
    const npcs = [...new Set(mechanics.flatMap((m) => (m.npcLanes ? m.npcLanes(summary) : [])))];
    const players = [...new Set([...(summary.players || []), ...mechanics.flatMap((m) => (m.playerLanes ? m.playerLanes(summary) : []))])]
      .sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }));
    return { tops, npcs, players };
  }

  // fight: { title, start, end, summary }. onSelect(mechanic, item) runs when a mark is clicked; clicking a mark of
  // the item already selected clears the selection, and onSelect gets a null item.
  // isShown(key) says whether a mechanic's marks are switched on; call refresh() after it changes.
  function create(container, fight, onSelect, isShown) {
    const origin = fight.start;
    const full = { from: -5, to: Math.max(30, fight.end - origin + 5) };
    let view = { ...full };
    let selected = null;
    const clipId = `plot-clip-${++instances}`;
    const { tops, npcs, players } = lanesOf(fight.summary);

    // Lanes never depend on the filter, so the timeline keeps its size as marks are switched on and off.
    const lanes = new Map();
    let height = AXIS_HEIGHT;
    for (const name of tops) {
      lanes.set(`top|${name}`, { kind: 'top', name, y: height, height: TOP_LANE_HEIGHT });
      height += TOP_LANE_HEIGHT;
    }
    for (const name of npcs) {
      lanes.set(`npc|${name}`, { kind: 'npc', name, y: height, height: NPC_LANE_HEIGHT });
      height += NPC_LANE_HEIGHT;
    }
    for (const name of players) {
      lanes.set(`player|${name}`, { kind: 'player', name, y: height, height: PLAYER_LANE_HEIGHT });
      height += PLAYER_LANE_HEIGHT;
    }
    height += PAD;

    const svg = el('svg', { role: 'img', 'aria-label': `Timeline for ${fight.title}`, height });
    container.replaceChildren(svg);

    function width() {
      return Math.max(LABEL_WIDTH + 200, container.clientWidth);
    }

    function draw() {
      const w = width();
      const x = (t) => LABEL_WIDTH + ((t - view.from) / (view.to - view.from)) * (w - LABEL_WIDTH - PAD);
      svg.setAttribute('viewBox', `0 0 ${w} ${height}`);
      svg.replaceChildren();

      let stripe = 0;
      let previous = null;
      for (const lane of lanes.values()) {
        // A rule between the top, NPC and player lanes.
        if (previous && previous.kind !== lane.kind) {
          stripe = 0;
          el('line', { class: 'lane-divider', x1: 0, x2: w, y1: lane.y + 0.5, y2: lane.y + 0.5 }, svg);
        }
        previous = lane;
        if (stripe++ % 2 === 0) el('rect', { class: 'lane-bg', x: 0, y: lane.y, width: w, height: lane.height }, svg);
        const label = el('text', { class: 'lane-label', x: 8, y: lane.y + lane.height / 2 + 4 }, svg);
        label.textContent = lane.name.length > 28 ? `${lane.name.slice(0, 27)}…` : lane.name;
        el('title', {}, label).textContent = lane.name;
      }

      const plot = el('g', {}, svg);
      const clip = el('clipPath', { id: clipId }, svg);
      el('rect', { x: LABEL_WIDTH, y: 0, width: w - LABEL_WIDTH, height }, clip);
      plot.setAttribute('clip-path', `url(#${clipId})`);

      const axis = el('g', { class: 'axis' }, svg);
      const step = niceStep(view.to - view.from, w - LABEL_WIDTH);
      for (let t = Math.ceil(view.from / step) * step; t <= view.to; t += step) {
        const gx = x(t);
        el('line', { class: 'grid', x1: gx, x2: gx, y1: AXIS_HEIGHT - 4, y2: height - PAD }, plot);
        el('text', { x: gx, y: 14, 'text-anchor': 'middle' }, axis).textContent = clock(t);
      }

      for (const mechanic of mechanics) {
        if (!isShown(mechanic.key) || !mechanic.draw) continue;
        mechanic.draw(fight.summary, {
          x: (epoch) => x(epoch - origin),
          topLane: (name) => lanes.get(`top|${name}`) || null,
          npcLane: (name) => lanes.get(`npc|${name}`) || null,
          playerLane: (name) => lanes.get(`player|${name}`) || null,
          el: (name, attrs, parent) => el(name, attrs, parent || plot),
          clock: (epoch) => clock(epoch - origin),
          isSelected: (item) => item === selected,
          selectable: (node, item) => node.addEventListener('click', (event) => {
            event.stopPropagation();
            if (dragged) return;
            selected = selected === item ? null : item;
            draw();
            onSelect(mechanic, selected);
          }),
        });
      }
    }

    // Zoom about the cursor; drag to pan.
    svg.addEventListener('wheel', (event) => {
      event.preventDefault();
      const w = width();
      const rect = svg.getBoundingClientRect();
      const px = ((event.clientX - rect.left) / rect.width) * w;
      if (px < LABEL_WIDTH) return;
      const t = view.from + ((px - LABEL_WIDTH) / (w - LABEL_WIDTH - PAD)) * (view.to - view.from);
      const factor = event.deltaY > 0 ? 1.25 : 0.8;
      const span = Math.min(full.to - full.from, Math.max(10, (view.to - view.from) * factor));
      const ratio = (t - view.from) / (view.to - view.from);
      view = clamp({ from: t - span * ratio, to: t - span * ratio + span });
      draw();
    }, { passive: false });

    // Dragging starts only after a few pixels, so a plain click still reaches the mark under the pointer.
    let drag = null;
    let dragged = false;
    svg.addEventListener('pointerdown', (event) => {
      drag = { x: event.clientX, view: { ...view }, active: false };
      dragged = false;
    });
    svg.addEventListener('pointermove', (event) => {
      if (!drag) return;
      if (!drag.active) {
        if (Math.abs(event.clientX - drag.x) < 4) return;
        drag.active = true;
        dragged = true;
        svg.setPointerCapture(event.pointerId);
        svg.classList.add('dragging');
      }
      const rect = svg.getBoundingClientRect();
      const plotWidth = (rect.width * (width() - LABEL_WIDTH - PAD)) / width();
      const dt = ((event.clientX - drag.x) / plotWidth) * (drag.view.to - drag.view.from);
      view = clamp({ from: drag.view.from - dt, to: drag.view.to - dt });
      draw();
    });
    const endDrag = () => {
      drag = null;
      svg.classList.remove('dragging');
    };
    svg.addEventListener('pointerup', endDrag);
    svg.addEventListener('pointercancel', endDrag);

    function clamp(next) {
      const span = next.to - next.from;
      if (next.from < full.from) return { from: full.from, to: full.from + span };
      if (next.to > full.to) return { from: full.to - span, to: full.to };
      return next;
    }

    const resize = new ResizeObserver(() => draw());
    resize.observe(container);
    draw();

    return {
      select(item) {
        selected = item;
        draw();
      },
      selected: () => selected,
      refresh() {
        draw();
      },
      reset() {
        view = { ...full };
        draw();
      },
      dispose() {
        resize.disconnect();
      },
    };
  }

  return { create, register, mechanics, lanesOf };
})();
