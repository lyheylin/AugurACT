'use strict';

// Buffs and detrimentals: a list the user keeps by hand under each recorded encounter on the Encounters data tab
// (EncounterRecord.effects in Core), with the effect's icon, tooltip screenshots, description and notes.
//
// A screenshot goes to the plugin as soon as it's added to the dialog ("add-effect-image"). The plugin saves it,
// cuts out the icon and reads the tooltip's text with Windows' OCR, and answers with "effect-image": the file
// names and the text, which fill the dialog's empty fields. Saving sends "save-effect" with the file names.
// Images load from their own host (AugurPage.config.effectImages). The up / down arrows order the list
// ("move-effect"), and descriptions and notes show {Name} references (refs.js).
(() => {
  const { make, plural, orderButtons, removeCell } = window.AugurUtil;
  const ctx = () => window.AugurPage.ctx;
  const { text: withRefs, imageUrl, KINDS } = window.AugurEffectRefs;

  // ---- the list on the Encounters data tab ----

  function recordSection(record) {
    const effects = record.effects || [];
    const box = make('div', 'fx-list');
    const head = make('h4', null, `Buffs and detrimentals (${effects.length})`);
    if (ctx()?.host) {
      const add = make('button', 'fx-add', 'Add +');
      add.type = 'button';
      add.title = 'Record a buff or detrimental of this encounter';
      add.addEventListener('click', () => open(record, null));
      head.appendChild(add);
    }
    box.appendChild(head);
    if (!effects.length) {
      box.appendChild(make('p', 'muted', 'None recorded. Add one with a screenshot of its tooltip, or by hand.'));
      return box;
    }

    const table = make('table', 'fx-table');
    const body = table.createTBody();
    for (const [index, effect] of effects.entries()) {
      const row = body.insertRow();
      row.insertCell().appendChild(iconOf(effect.icon, 'fx-icon'));

      const main = row.insertCell();
      main.className = 'fx-main';
      const title = make('div', 'fx-title');
      title.append(make('strong', null, effect.name), kindTag(effect.kind));
      main.appendChild(title);
      if (effect.description) main.appendChild(withRefs(effect.description, record, 'fx-description'));
      if (effect.notes) main.appendChild(withRefs(effect.notes, record, 'fx-notes'));

      const shots = row.insertCell();
      shots.className = 'fx-shots';
      for (const file of effect.screenshots || []) shots.appendChild(thumbnail(file, effect.name));

      const actions = row.insertCell();
      actions.className = 'fx-actions';
      if (!ctx()?.host) continue;
      for (const [label, title, run] of [
        ['Edit', 'Change it, or add screenshots', () => open(record, effect)],
      ]) {
        const button = make('button', null, label);
        button.type = 'button';
        button.title = title;
        button.addEventListener('click', (event) => {
          event.stopPropagation();
          run();
        });
        actions.appendChild(button);
      }
      const order = row.insertCell();
      order.className = 'order-cell';
      order.appendChild(orderButtons(index, effects.length,
        (offset) => ctx().send({ type: 'move-effect', zone: record.zone, name: record.name, effect: effect.name, offset }), 'this effect'));
      removeCell(row, `Remove ${effect.name} from ${record.name}`, () => {
        if (confirm(`Remove ${effect.name} from ${record.name}?`)) ctx().send({ type: 'remove-effect', zone: record.zone, name: record.name, effect: effect.name });
      });
    }
    box.appendChild(table);
    return box;
  }

  function kindTag(kind) {
    return make('span', `fx-kind fx-${kind}`, KINDS[kind] || kind);
  }

  function iconOf(file, className) {
    if (!file) return make('span', `${className} fx-no-icon`);
    const img = make('img', className);
    img.src = imageUrl(file);
    img.alt = '';
    return img;
  }

  function thumbnail(file, name) {
    const button = make('button', 'fx-thumb');
    button.type = 'button';
    button.title = 'Show the screenshot';
    const img = make('img');
    img.src = imageUrl(file);
    img.alt = `Screenshot of ${name}`;
    button.appendChild(img);
    button.addEventListener('click', (event) => {
      event.stopPropagation();
      view(file, name);
    });
    return button;
  }

  // ---- the full-size screenshot ----

  let viewer = null;
  function view(file, name) {
    if (!viewer) {
      viewer = make('dialog', 'fx-viewer');
      viewer.setAttribute('aria-label', 'Screenshot');
      const form = make('form');
      form.method = 'dialog';
      const img = make('img');
      const close = make('button', null, 'Close');
      close.type = 'submit';
      const buttons = make('div', 'dialog-buttons');
      buttons.appendChild(close);
      form.append(img, buttons);
      viewer.appendChild(form);
      viewer.addEventListener('click', (event) => {
        if (event.target === viewer) viewer.close();
      });
      document.body.appendChild(viewer);
    }
    const img = viewer.querySelector('img');
    img.src = imageUrl(file);
    img.alt = `Screenshot of ${name}`;
    viewer.showModal();
  }

  // ---- the add / edit dialog ----

  // Static markup only: names and text are set with textContent and value.
  const MARKUP = `
<dialog id="fx-dialog" aria-labelledby="fx-title">
  <form method="dialog">
    <h2 id="fx-title">Add buff or detrimental</h2>
    <p id="fx-encounter" class="muted"></p>
    <div class="fx-row">
      <fieldset class="fx-kinds">
        <legend class="sr-only">Kind</legend>
        <label><input type="radio" name="fx-kind" value="detrimental" checked> Detrimental</label>
        <label><input type="radio" name="fx-kind" value="buff"> Buff</label>
      </fieldset>
      <label class="text-field fx-name"><span>Name</span><input id="fx-name" type="text" autocomplete="off" spellcheck="false"></label>
      <div class="fx-icon-field">
        <span class="fx-label">Icon</span>
        <div class="fx-icon-row">
          <span id="fx-icon-preview"></span>
          <button type="button" id="fx-icon-choose" title="Use an image file as the icon">Choose…</button>
          <button type="button" id="fx-icon-remove" class="remove-x" title="Remove the icon" aria-label="Remove the icon">&times;</button>
        </div>
      </div>
    </div>
    <div class="text-field">
      <span>Screenshots</span>
      <div id="fx-drop" class="fx-drop" tabindex="0">
        <div id="fx-shots" class="fx-shots"></div>
        <p class="muted hint">Paste a tooltip screenshot (Ctrl+V), drop one here, or <button type="button" id="fx-shot-choose">choose a file</button>.
          The icon and the text are read from it.</p>
      </div>
    </div>
    <p id="fx-status" class="muted hint" role="status"></p>
    <label class="text-field"><span>Description</span>
      <textarea id="fx-description" rows="7" placeholder="What the tooltip says it does"></textarea>
    </label>
    <label class="text-field"><span>Notes</span>
      <textarea id="fx-notes" rows="3" placeholder="Your notes: how to handle it, who does what. Write {Name} to refer to another buff or detrimental."></textarea>
    </label>
    <p id="fx-error" role="alert"></p>
    <div class="dialog-buttons">
      <button type="submit" value="cancel" formnovalidate>Cancel</button>
      <button type="submit" value="save" id="fx-save" class="primary">Save</button>
    </div>
  </form>
  <input type="file" id="fx-shot-file" accept="image/*" multiple hidden>
  <input type="file" id="fx-icon-file" accept="image/*" hidden>
</dialog>`;

  let dialog = null;
  const field = (id) => document.getElementById(id);
  // { record, effect, icon, screenshots: [file], pending: Map(requestId -> 'shot' | 'icon') }
  let state = null;
  let nextRequest = 1;

  function open(record, effect) {
    build();
    state = { record, effect, icon: effect?.icon || null, screenshots: [...(effect?.screenshots || [])], pending: new Map() };
    field('fx-title').textContent = effect ? `Edit ${effect.name}` : 'Add buff or detrimental';
    field('fx-encounter').textContent = `${record.name} · ${record.zone}`;
    dialog.querySelector(`input[name="fx-kind"][value="${effect?.kind === 'buff' ? 'buff' : 'detrimental'}"]`).checked = true;
    field('fx-name').value = effect?.name || '';
    field('fx-description').value = effect?.description || '';
    field('fx-notes').value = effect?.notes || '';
    field('fx-error').textContent = '';
    setStatus('');
    showImages();
    dialog.showModal();
    (effect ? field('fx-name') : field('fx-drop')).focus();
  }

  function setStatus(text, action) {
    const status = field('fx-status');
    status.replaceChildren(document.createTextNode(text));
    if (action) {
      const button = make('button', 'fx-inline', action.label);
      button.type = 'button';
      button.addEventListener('click', action.run);
      status.append(' ', button);
    }
  }

  function showImages() {
    field('fx-icon-preview').replaceChildren(iconOf(state.icon, 'fx-icon'));
    field('fx-icon-remove').disabled = !state.icon;
    const shots = field('fx-shots');
    shots.replaceChildren();
    for (const file of state.screenshots) {
      const item = make('div', 'fx-shot');
      const img = make('img');
      img.src = imageUrl(file);
      img.alt = 'Screenshot';
      const remove = make('button', 'fx-shot-remove', '×');
      remove.type = 'button';
      remove.title = 'Remove this screenshot';
      remove.setAttribute('aria-label', 'Remove this screenshot');
      remove.addEventListener('click', () => {
        state.screenshots = state.screenshots.filter((f) => f !== file);
        showImages();
      });
      item.append(img, remove);
      shots.appendChild(item);
    }
    const busy = [...state.pending.values()].filter((p) => p === 'shot').length;
    for (let i = 0; i < busy; i++) shots.appendChild(make('div', 'fx-shot fx-shot-busy', 'Reading…'));
  }

  function sendImage(file, asIcon) {
    if (!file || !file.type.startsWith('image/')) {
      setStatus('That isn’t an image.');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      if (!state) return;
      const requestId = `fx${nextRequest++}`;
      state.pending.set(requestId, asIcon ? 'icon' : 'shot');
      setStatus(asIcon ? 'Saving the icon…' : 'Reading the screenshot…');
      showImages();
      ctx().send({ type: 'add-effect-image', requestId, data: reader.result, asIcon });
    };
    reader.readAsDataURL(file);
  }

  // The plugin's answer to "add-effect-image". Fills only what the user hasn't: a name, a description and an
  // icon already there stay, and the text read is offered instead.
  function onImage(data) {
    if (!state || !state.pending.has(data.requestId)) return;
    const what = state.pending.get(data.requestId);
    state.pending.delete(data.requestId);
    if (data.error) {
      showImages();
      setStatus(data.error);
      return;
    }
    if (what === 'icon') {
      state.icon = data.file;
      showImages();
      setStatus('');
      return;
    }

    if (!state.screenshots.includes(data.file)) state.screenshots.push(data.file);
    const found = [];
    if (data.icon && !state.icon) {
      state.icon = data.icon;
      found.push('icon');
    }
    const name = field('fx-name');
    if (data.name && !name.value.trim()) {
      name.value = data.name;
      found.push('name');
    }
    const description = field('fx-description');
    let offer = null;
    if (data.description && !description.value.trim()) {
      description.value = data.description;
      found.push('description');
    } else if (data.description && description.value.trim() !== data.description) {
      offer = { label: 'Use the text read instead', run: () => {
        description.value = data.description;
        setStatus('Description replaced with the text read. Check it against the screenshot.');
      } };
    }
    showImages();

    const parts = [];
    if (found.length) parts.push(`Filled in the ${list(found)} from the screenshot.`);
    if (found.includes('description')) parts.push('OCR is a draft: check the numbers and words against the screenshot.');
    if (data.warning) parts.push(data.warning);
    if (!found.length && !offer && !data.warning) parts.push('Screenshot added.');
    setStatus(parts.join(' '), offer);
  }

  function list(words) {
    return words.length < 2 ? words.join('') : `${words.slice(0, -1).join(', ')} and ${words[words.length - 1]}`;
  }

  function imagesFrom(transfer) {
    return [...(transfer?.files || [])].filter((f) => f.type.startsWith('image/'));
  }

  function build() {
    if (dialog) return;
    const holder = document.createElement('div');
    holder.innerHTML = MARKUP;
    dialog = holder.firstElementChild;
    document.body.appendChild(dialog);

    field('fx-shot-choose').addEventListener('click', () => field('fx-shot-file').click());
    field('fx-shot-file').addEventListener('change', (event) => {
      for (const file of event.target.files) sendImage(file, false);
      event.target.value = '';
    });
    field('fx-icon-choose').addEventListener('click', () => field('fx-icon-file').click());
    field('fx-icon-file').addEventListener('change', (event) => {
      sendImage(event.target.files[0], true);
      event.target.value = '';
    });
    field('fx-icon-remove').addEventListener('click', () => {
      state.icon = null;
      showImages();
    });

    // A screenshot pasted anywhere in the dialog, except as text into a text box.
    dialog.addEventListener('paste', (event) => {
      const images = imagesFrom(event.clipboardData);
      if (!images.length) return;
      event.preventDefault();
      for (const file of images) sendImage(file, false);
    });
    const drop = field('fx-drop');
    drop.addEventListener('dragover', (event) => {
      event.preventDefault();
      drop.classList.add('over');
    });
    drop.addEventListener('dragleave', () => drop.classList.remove('over'));
    drop.addEventListener('drop', (event) => {
      event.preventDefault();
      drop.classList.remove('over');
      for (const file of imagesFrom(event.dataTransfer)) sendImage(file, false);
    });

    // Handled on submit, not on "close" (see the update dialog in app.js).
    dialog.querySelector('form').addEventListener('submit', (event) => {
      const current = state;
      if (event.submitter?.value !== 'save' || !current) {
        state = null;
        return;
      }
      const name = field('fx-name').value.trim();
      if (!name) {
        event.preventDefault();
        field('fx-error').textContent = 'Give it a name.';
        field('fx-name').focus();
        return;
      }
      if (current.pending.size) {
        event.preventDefault();
        field('fx-error').textContent = `Wait for ${plural(current.pending.size, 'image')} to finish saving.`;
        return;
      }
      state = null;
      ctx().send({
        type: 'save-effect',
        zone: current.record.zone,
        name: current.record.name,
        entry: {
          original: current.effect?.name ?? null,
          kind: dialog.querySelector('input[name="fx-kind"]:checked').value,
          name,
          description: field('fx-description').value.trim() || null,
          notes: field('fx-notes').value.trim() || null,
          icon: current.icon,
          screenshots: current.screenshots,
        },
      });
    });
  }

  window.AugurPage.register({
    key: 'effects',
    recordSection,
    messages: { 'effect-image': onImage },
  });
})();
