'use strict';

// Small helpers shared by the page and the mechanic files. Log text holds player names and chat, so
// everything from it is written with textContent (make's text argument does that).
window.AugurUtil = (() => {
  function make(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  }

  function time(epoch) {
    return new Date(epoch * 1000).toLocaleTimeString(undefined, { hour12: false });
  }

  function clock(seconds) {
    const sign = seconds < 0 ? '-' : '';
    const s = Math.abs(Math.round(seconds));
    return `${sign}${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  }

  function damage(value) {
    for (const [scale, suffix] of [[1e15, 'Q'], [1e12, 'T'], [1e9, 'B'], [1e6, 'M'], [1e3, 'K']]) {
      if (value >= scale) return `${(value / scale).toFixed(1)}${suffix}`;
    }
    return String(Math.round(value));
  }

  function fmtSeconds(value) {
    return value == null ? '-' : `${Math.round(value * 10) / 10} s`;
  }

  function median(values) {
    const sorted = values.filter((v) => v != null).sort((a, b) => a - b);
    if (!sorted.length) return null;
    const mid = Math.floor(sorted.length / 2);
    return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
  }

  function plural(n, word) {
    return `${n} ${word}${n === 1 ? '' : 's'}`;
  }

  function escapeRegex(text) {
    return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }

  // Fills a detail list with log lines; classify(text) gives each line's class. With the fight's start, each
  // line's time is followed by the time into the fight: "22:53:09 (0:11)".
  function fillLines(list, lines, classify, start) {
    list.replaceChildren();
    for (const line of lines || []) {
      const li = make('li', classify ? classify(line.text) : null);
      const at = make('time', null, time(line.at));
      if (start != null) at.appendChild(make('span', 'elapsed', ` (${clock(line.at - start)})`));
      li.append(at, document.createTextNode(line.text));
      list.appendChild(li);
    }
  }

  // Small up / down arrows, stacked, for moving an entry of a list the user orders. move(offset) gets -1 or 1;
  // an arrow that would move past either end is disabled.
  function orderButtons(index, count, move, what) {
    const box = make('span', 'order');
    for (const [offset, glyph, label] of [[-1, '\u25B2', 'up'], [1, '\u25BC', 'down']]) {
      const button = make('button', null, glyph);
      button.type = 'button';
      button.title = `Move ${what} ${label}`;
      button.setAttribute('aria-label', button.title);
      button.disabled = index + offset < 0 || index + offset >= count;
      button.addEventListener('click', (event) => {
        event.stopPropagation();
        move(offset);
      });
      box.appendChild(button);
    }
    return box;
  }

  return { make, time, clock, damage, fmtSeconds, median, plural, escapeRegex, fillLines, orderButtons };
})();
