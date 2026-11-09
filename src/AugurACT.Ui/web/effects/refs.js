'use strict';

// References to buffs and detrimentals in the user's own text: "Gains {Eye of the Griffin} buff." Wherever a
// description or note is shown, AugurEffectRefs.text() writes it with each {Name} that names a recorded effect as
// a link; hovering or focusing it shows that effect the way the game shows a tooltip, built from the entry (its
// icon, name, description and notes), not from a screenshot.
//
// A name is looked up in the encounter the text belongs to first, then in every recorded encounter. A {Name}
// that matches nothing stays as it was typed. Also shared with effects.js: imageUrl(file) and the KINDS labels.
window.AugurEffectRefs = (() => {
  const { make } = window.AugurUtil;
  const REF = /\{([^{}\n]+)\}/g;
  const KINDS = { detrimental: 'Detrimental', buff: 'Buff' };
  const imageUrl = (file) => `${window.AugurPage.config?.effectImages || 'https://augur-images.act/'}${encodeURIComponent(file)}`;

  function find(name, record) {
    const key = name.trim().toLowerCase();
    const match = (r) => (r?.effects || []).find((e) => e.name.trim().toLowerCase() === key);
    const own = match(record);
    if (own) return own;
    for (const other of window.AugurPage.ctx?.getData()?.encounters || []) {
      const effect = match(other);
      if (effect) return effect;
    }
    return null;
  }

  // The text as a node with the given class. Inside a tooltip (link: false) names are only coloured, so tooltips
  // don't open from tooltips.
  function text(value, record, className, link = true) {
    const box = make('div', className);
    let at = 0;
    for (const m of value.matchAll(REF)) {
      const effect = find(m[1], record);
      if (!effect) continue;
      if (m.index > at) box.appendChild(document.createTextNode(value.slice(at, m.index)));
      box.appendChild(link ? reference(m[0], effect) : make('span', `fx-ref-name fx-${effect.kind}`, m[0]));
      at = m.index + m[0].length;
    }
    if (at < value.length) box.appendChild(document.createTextNode(value.slice(at)));
    return box;
  }

  function reference(label, effect) {
    const span = make('span', `fx-ref fx-${effect.kind}`, label);
    span.tabIndex = 0;
    span.setAttribute('aria-describedby', 'fx-tip');
    const show = () => open(span, effect);
    span.addEventListener('mouseenter', show);
    span.addEventListener('focus', show);
    span.addEventListener('mouseleave', close);
    span.addEventListener('blur', close);
    // A reference sits inside rows that expand or select on click.
    span.addEventListener('click', (event) => event.stopPropagation());
    return span;
  }

  // ---- the tooltip ----

  let tip = null;

  function build() {
    if (tip) return;
    tip = make('div', 'fx-tip');
    tip.id = 'fx-tip';
    tip.setAttribute('role', 'tooltip');
    tip.hidden = true;
    document.body.appendChild(tip);
    window.addEventListener('scroll', close, true);
    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') close();
    });
  }

  // Laid out like the game's: name and icon on top, then "Effects:" and the description's lines, bullets kept.
  function fill(effect) {
    tip.replaceChildren();
    const head = make('div', 'fx-tip-head');
    const title = make('div', 'fx-tip-title');
    title.append(make('div', 'fx-tip-name', effect.name), make('div', `fx-tip-kind fx-${effect.kind}`, KINDS[effect.kind] || effect.kind));
    head.appendChild(title);
    if (effect.icon) {
      const icon = make('img', 'fx-tip-icon');
      icon.src = imageUrl(effect.icon);
      icon.alt = '';
      head.appendChild(icon);
    }
    tip.appendChild(head);

    if (effect.description) {
      tip.appendChild(make('div', 'fx-tip-label', 'Effects:'));
      const list = make('ul', 'fx-tip-effects');
      for (const line of effect.description.split('\n')) {
        if (!line.trim()) continue;
        const bullet = /^\s*[•·*-]\s*/.exec(line);
        const item = text(bullet ? line.slice(bullet[0].length) : line, null, null, false);
        const li = make('li', bullet ? null : 'fx-tip-plain');
        li.append(...item.childNodes);
        list.appendChild(li);
      }
      tip.appendChild(list);
    }
    if (effect.notes) {
      tip.appendChild(make('div', 'fx-tip-label', 'Notes:'));
      tip.appendChild(text(effect.notes, null, 'fx-tip-notes', false));
    }
    if (!effect.description && !effect.notes) tip.appendChild(make('div', 'fx-tip-notes', 'No description recorded.'));
  }

  function open(anchor, effect) {
    build();
    fill(effect);
    tip.hidden = false;
    const a = anchor.getBoundingClientRect();
    const t = tip.getBoundingClientRect();
    const margin = 8;
    const left = Math.max(margin, Math.min(a.left, window.innerWidth - t.width - margin));
    const below = a.bottom + 6;
    const top = below + t.height <= window.innerHeight - margin ? below : Math.max(margin, a.top - t.height - 6);
    tip.style.left = `${left}px`;
    tip.style.top = `${top}px`;
  }

  function close() {
    if (tip) tip.hidden = true;
  }

  return { text, imageUrl, KINDS };
})();
