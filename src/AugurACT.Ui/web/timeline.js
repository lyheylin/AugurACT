'use strict';

// Draws barrage casts as one lane per NPC: a bar from warning to release, coloured by outcome, with
// bulwark presses as ticks beneath it. Written with DOM calls only, so log text never becomes markup.
window.AugurTimeline = (() => {
  const SVG = 'http://www.w3.org/2000/svg';
  const LABEL_WIDTH = 190;
  const LANE_HEIGHT = 34;
  const AXIS_HEIGHT = 22;
  const PAD = 12;

  function el(name, attrs, parent) {
    const node = document.createElementNS(SVG, name);
    for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
    if (parent) parent.appendChild(node);
    return node;
  }

  function clock(seconds) {
    const sign = seconds < 0 ? '-' : '';
    const s = Math.abs(Math.round(seconds));
    return `${sign}${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  }

  function niceStep(span, width) {
    const target = span / Math.max(2, width / 90);
    for (const step of [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600]) {
      if (step >= target) return step;
    }
    return 7200;
  }

  function create(container, fight, onSelect) {
    const lanes = [...new Set(fight.barrages.map((b) => b.npc))];
    const origin = fight.start;
    const full = { from: -5, to: Math.max(30, fight.end - origin + 5) };
    let view = { ...full };
    let selected = null;

    const height = AXIS_HEIGHT + lanes.length * LANE_HEIGHT + PAD;
    const svg = el('svg', { height, role: 'img', 'aria-label': `Barrage timeline for ${fight.title}` });
    container.replaceChildren(svg);

    function width() {
      return Math.max(LABEL_WIDTH + 200, container.clientWidth);
    }

    function x(t, w) {
      return LABEL_WIDTH + ((t - view.from) / (view.to - view.from)) * (w - LABEL_WIDTH - PAD);
    }

    function draw() {
      const w = width();
      svg.setAttribute('viewBox', `0 0 ${w} ${height}`);
      svg.replaceChildren();

      lanes.forEach((name, i) => {
        const y = AXIS_HEIGHT + i * LANE_HEIGHT;
        if (i % 2 === 0) el('rect', { class: 'lane-bg', x: 0, y, width: w, height: LANE_HEIGHT }, svg);
        const label = el('text', { class: 'lane-label', x: 8, y: y + LANE_HEIGHT / 2 + 4 }, svg);
        label.textContent = name.length > 28 ? `${name.slice(0, 27)}…` : name;
        el('title', {}, label).textContent = name;
      });

      const plot = el('g', {}, svg);
      const clip = el('clipPath', { id: 'plot-clip' }, svg);
      el('rect', { x: LABEL_WIDTH, y: 0, width: w - LABEL_WIDTH, height }, clip);
      plot.setAttribute('clip-path', 'url(#plot-clip)');

      const axis = el('g', { class: 'axis' }, svg);
      const step = niceStep(view.to - view.from, w - LABEL_WIDTH);
      for (let t = Math.ceil(view.from / step) * step; t <= view.to; t += step) {
        const gx = x(t, w);
        el('line', { class: 'grid', x1: gx, x2: gx, y1: AXIS_HEIGHT - 4, y2: height - PAD }, plot);
        const label = el('text', { x: gx, y: 14, 'text-anchor': 'middle' }, axis);
        label.textContent = clock(t);
      }

      for (const cast of fight.barrages) {
        const lane = lanes.indexOf(cast.npc);
        const y = AXIS_HEIGHT + lane * LANE_HEIGHT;
        const start = cast.readiedAt - origin;
        const end = (cast.resolvedAt ?? cast.readiedAt + 10) - origin;
        const x1 = x(start, w);
        const x2 = Math.max(x1 + 3, x(end, w));
        const outcome = cast.outcome.toLowerCase();

        const bar = el('rect', {
          class: `cast ${outcome}${cast === selected ? ' selected' : ''}`,
          x: x1, y: y + 6, width: x2 - x1, height: 14, rx: 2,
        }, plot);
        el('title', {}, bar).textContent = describe(cast, origin);
        bar.addEventListener('click', (event) => {
          event.stopPropagation();
          if (dragged) return;
          selected = cast;
          draw();
          onSelect(cast);
        });

        if (cast.outcome !== 'Cancelled' && cast.resolvedAt != null) {
          const rx = x(end, w);
          el('path', { class: 'release', d: `M${rx} ${y + 3} l4 4 l-4 4 l-4 -4 z` }, plot);
        }

        for (const second of new Set((cast.bulwarks || []).map((b) => b.at))) {
          const late = cast.resolvedAt != null && second > cast.resolvedAt;
          const bx = x(second - origin, w);
          el('line', { class: `bulwark${late ? ' late' : ''}`, x1: bx, x2: bx, y1: y + 22, y2: y + 31 }, plot);
        }
      }
    }

    function describe(cast, start) {
      const at = clock(cast.readiedAt - start);
      const target = cast.target ? ` at ${cast.target}` : '';
      return `${at} ${cast.npc}${target}: ${cast.outcome}`;
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

    // Dragging starts only after a few pixels, so a plain click still reaches the bar under the pointer.
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
      select(cast) {
        selected = cast;
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

  return { create, clock };
})();
