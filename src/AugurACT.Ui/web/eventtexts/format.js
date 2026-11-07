'use strict';

// Event text helpers shared by the timeline (mechanics/eventtext.js), the dialog and the lists: the template
// format, timers, and which recorded encounter a fight belongs to. No DOM state here.
//
// A template is the text with variables written as {player}, {class}, {archetype} or {number}, and a literal
// brace doubled. It must read the same as EventTextTemplate in Core, which uses it to apply the blacklist.
window.AugurEventTexts = (() => {
  const { make, plural, clock } = window.AugurUtil;

  const KINDS = { emote: 'Emote', say: 'NPC say', narration: 'Narration' };
  const SLOTS = ['player', 'class', 'archetype', 'number'];
  const ARCHETYPES = ['Fighter', 'Priest', 'Scout', 'Mage'];
  const CLASSES = [
    'Guardian', 'Berserker', 'Monk', 'Bruiser', 'Shadowknight', 'Paladin',
    'Templar', 'Inquisitor', 'Warden', 'Fury', 'Mystic', 'Defiler', 'Channeler',
    'Swashbuckler', 'Brigand', 'Troubadour', 'Dirge', 'Ranger', 'Assassin', 'Beastlord',
    'Wizard', 'Warlock', 'Illusionist', 'Coercer', 'Conjuror', 'Necromancer',
  ];
  const NUMBER = '\\d+(?:[.,]\\d+)*[KMBTQ]?';
  const PATTERNS = { player: '[A-Za-z]+', number: NUMBER, class: CLASSES.join('|'), archetype: ARCHETYPES.join('|') };

  // Parts of plain text that could be variables. Player names can't be told from other words here; the user
  // marks them, or they come from the encounter's player list.
  const CANDIDATE = new RegExp(
    `(?<![A-Za-z0-9])(?:(?<class>${PATTERNS.class})|(?<archetype>${PATTERNS.archetype})|(?<number>${NUMBER}))(?![A-Za-z0-9])`, 'gi');


  // ---- template format ----

  const escapeLiteral = (text) => text.replace(/\{/g, '{{').replace(/\}/g, '}}');
  const escapeRegex = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

  // [{ text } | { slot }], or { error }.
  function parse(template) {
    if (!template || !template.trim()) return { error: 'The text is empty.' };
    const parts = [];
    let literal = '';
    const token = /\{\{|\}\}|\{([a-z]+)\}|[{}]/g;
    let at = 0;
    let m;
    while ((m = token.exec(template))) {
      literal += template.slice(at, m.index);
      at = token.lastIndex;
      if (m[0] === '{{' || m[0] === '}}') { literal += m[0][0]; continue; }
      if (!SLOTS.includes(m[1])) {
        return { error: m[0].length === 1 ? 'A single { or } must be doubled, as {{ or }}.' : `Unknown variable ${m[0]}. Use {player}, {class}, {archetype} or {number}.` };
      }
      if (literal) parts.push({ text: literal });
      literal = '';
      parts.push({ slot: m[1] });
    }
    literal += template.slice(at);
    if (literal) parts.push({ text: literal });
    return { parts };
  }

  // Matches whole lines without markup, ignoring case, like EventTextTemplate.ToRegex.
  function toRegex(template) {
    const parsed = parse(template);
    if (parsed.error) return null;
    return new RegExp(`^${parsed.parts.map((p) => (p.slot ? `(${PATTERNS[p.slot]})` : escapeRegex(p.text))).join('')}$`, 'i');
  }

  // The template drawn with its variables as chips.
  function render(template) {
    const node = make('span', 'et-text');
    const parsed = parse(template);
    if (parsed.error) {
      node.textContent = template;
      return node;
    }
    for (const part of parsed.parts) {
      node.appendChild(part.slot ? make('span', 'et-var', `{${part.slot}}`) : document.createTextNode(part.text));
    }
    return node;
  }

  function kindTag(kind) {
    const tag = make('span', `et-kind et-${kind}`);
    tag.append(make('i', `et-swatch et-${kind}`), document.createTextNode(KINDS[kind] || kind));
    return tag;
  }

  // ---- timers ----

  // "3:40 ± 13 s", or "20 s ± 2 s".
  function formatTimer(mean, spread) {
    const m = mean >= 60 ? clock(mean) : `${Math.round(mean)} s`;
    return `${m} ± ${Math.round(spread)} s`;
  }

  function describeRecorded(timer) {
    const fights = timer.fights?.length || 0;
    return fights ? `from ${plural(fights, 'fight')}, ${plural(timer.intervals, 'interval')}` : 'typed by hand';
  }

  // "3:40", "220" or "220.5" seconds; NaN when unreadable.
  function readSeconds(text) {
    const value = (text || '').trim();
    const m = value.match(/^(\d+):(\d{1,2}(?:\.\d+)?)$/);
    if (m) return Number(m[1]) * 60 + Number(m[2]);
    return /^\d+(?:\.\d+)?$/.test(value) ? Number(value) : NaN;
  }

  const secondsText = (value) => (value >= 60 ? clock(value) : String(Math.round(value * 10) / 10));

  // A recorded timer with one more fight's measurement in it, weighted by intervals. The ± widens to cover
  // both; a timer typed by hand counts as one interval.
  function mergeTimer(recorded, measured, fight) {
    const fresh = { mean: measured.mean, spread: measured.spread, intervals: measured.events - 1, fights: [fight] };
    if (!recorded) return fresh;
    if ((recorded.fights || []).includes(fight)) return { ...recorded };
    const n = recorded.intervals || 1;
    const m = fresh.intervals;
    const mean = (recorded.mean * n + measured.mean * m) / (n + m);
    const lo = Math.min(recorded.mean - recorded.spread, measured.min);
    const hi = Math.max(recorded.mean + recorded.spread, measured.max);
    return {
      mean: Math.round(mean * 10) / 10,
      spread: Math.round(Math.max(mean - lo, hi - mean) * 10) / 10,
      intervals: recorded.intervals + m,
      fights: [...(recorded.fights || []), fight],
    };
  }

  // ---- matching ACT fights to recorded encounters ----

  // Recorded encounters in the fight's zone whose bosses appear in it, best first.
  function matchRecords(encounter, summary) {
    const records = window.AugurPage.ctx?.getData()?.encounters || [];
    const zone = (encounter.zone || '').toLowerCase();
    const names = new Set((encounter.title || '').toLowerCase().split(', '));
    for (const c of summary?.barrages || []) names.add(c.npc.toLowerCase());
    for (const g of summary?.eventTexts || []) for (const o of g.occurrences) if (o.npc) names.add(o.npc.toLowerCase());
    return records
      .filter((r) => r.zone.toLowerCase() === zone)
      .map((r) => ({ record: r, hits: r.bosses.filter((b) => names.has(b.name.toLowerCase())).length }))
      .filter((m) => m.hits > 0)
      .sort((a, b) => (b.hits === b.record.bosses.length) - (a.hits === a.record.bosses.length) || b.hits - a.hits)
      .map((m) => m.record);
  }

  // The recorded entry that already covers a group from this fight, if any.
  function recordedEntry(record, group) {
    for (const entry of record?.eventTexts || []) {
      const rule = toRegex(entry.text);
      if (rule && group.occurrences.some((o) => rule.test(o.text))) return entry;
    }
    return null;
  }

  return {
    KINDS, SLOTS, CANDIDATE, escapeLiteral, parse, toRegex, render, kindTag,
    formatTimer, describeRecorded, readSeconds, secondsText, mergeTimer, matchRecords, recordedEntry,
  };
})();
