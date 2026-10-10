// Admin panel helpers.
(function () {
  'use strict';

  document.querySelectorAll('form[data-confirm]').forEach((form) => {
    form.addEventListener('submit', (event) => {
      if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) event.preventDefault();
    });
  });

  // Submit buttons that need a confirmation of their own (several actions sharing one form).
  document.querySelectorAll('button[data-confirm]').forEach((button) => {
    button.addEventListener('click', (event) => {
      if (button.dataset.confirm && !window.confirm(button.dataset.confirm)) event.preventDefault();
    });
  });

  // Standing order terms: choosing a vorta to add fills in its catalog wholesale price.
  const priceSource = document.querySelector('[data-price-source]');
  const priceTarget = document.querySelector('[data-price-target]');
  if (priceSource && priceTarget) {
    priceSource.addEventListener('change', () => {
      const option = priceSource.selectedOptions[0];
      const price = option ? option.dataset.price || '' : '';
      priceTarget.placeholder = price || 'catalog';
      if (!priceTarget.value || priceTarget.dataset.autofilled === 'true') {
        priceTarget.value = price;
        priceTarget.dataset.autofilled = 'true';
      }
    });
    priceTarget.addEventListener('input', () => { priceTarget.dataset.autofilled = 'false'; });
  }

  // Menu item: the default restaurant price follows the retail price until a wholesale price is entered.
  const retail = document.querySelector('[data-retail-price]');
  const wholesale = document.querySelector('[data-wholesale-price]');
  const wholesaleDefault = document.querySelector('[data-wholesale-default]');
  if (retail && wholesale) {
    retail.addEventListener('input', () => {
      const price = parseFloat(retail.value);
      if (!(price > 0)) return;
      const discount = parseFloat(wholesale.dataset.discount || '0');
      const value = (Math.round(price * (100 - discount)) / 100).toFixed(2);
      wholesale.placeholder = value;
      if (wholesaleDefault) wholesaleDefault.textContent = '$' + value;
    });
  }

  // Live slug preview: mirrors the server-side slug rules.
  const slugify = (text) => text.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
    .replace(/&/g, ' and ').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 120);
  const source = document.querySelector('[data-slug-source]');
  const target = document.querySelector('[data-slug-target]');
  const preview = document.querySelector('[data-slug-preview]');
  const refresh = () => {
    if (!preview) return;
    preview.textContent = slugify(target && target.value ? target.value : (source ? source.value : ''));
  };
  if (source) source.addEventListener('input', refresh);
  if (target) target.addEventListener('input', refresh);
  refresh();

  // Settings editor: show when there are unsaved changes and warn before leaving the page with them.
  const settingsForm = document.querySelector('form[data-settings-form]');
  if (settingsForm) {
    const status = settingsForm.querySelector('[data-dirty-status]');
    let submitting = false;
    const markDirty = () => {
      if (settingsForm.hasAttribute('data-dirty') && status && status.textContent) return;
      settingsForm.setAttribute('data-dirty', 'true');
      if (status) status.textContent = 'You have unsaved changes.';
    };
    settingsForm.addEventListener('input', markDirty);
    settingsForm.addEventListener('change', markDirty);
    settingsForm.addEventListener('submit', () => { submitting = true; });
    window.addEventListener('beforeunload', (event) => {
      if (submitting || !settingsForm.hasAttribute('data-dirty')) return;
      event.preventDefault();
      event.returnValue = '';
    });

    // "Remove saved password" empties and locks the password box so it is clear what will happen.
    settingsForm.querySelectorAll('input[data-secret-clear]').forEach((box) => {
      const input = document.getElementById(box.dataset.secretClear);
      if (!input) return;
      const sync = () => {
        input.disabled = box.checked;
        if (box.checked) input.value = '';
      };
      box.addEventListener('change', sync);
      sync();
    });

    // A link in the error summary may point into the collapsed "Advanced settings".
    settingsForm.querySelectorAll('.settings-error-summary a[href^="#"]').forEach((link) => {
      link.addEventListener('click', () => {
        const target = document.getElementById(link.getAttribute('href').slice(1));
        const details = target && target.closest('details');
        if (details) details.open = true;
        if (target && target.focus) setTimeout(() => target.focus(), 0);
      });
    });
  }

  // Inventory: suggest saved items with their usual unit, add and remove rows, show the price per unit and the total.
  const lines = document.querySelector('[data-inventory-lines]');
  if (lines) {
    const body = lines.querySelector('tbody');
    const totalEl = lines.querySelector('[data-lines-total]');
    const key = (name) => name.trim().replace(/\s+/g, ' ').toLowerCase();
    const usualUnits = new Map();
    const list = document.getElementById(lines.dataset.itemList);
    if (list) list.querySelectorAll('option').forEach((o) => usualUnits.set(key(o.value), o.dataset.unit || ''));
    const money = (n) => '$' + n.toFixed(2);

    const refresh = () => {
      let total = 0;
      body.querySelectorAll('[data-line]').forEach((row) => {
        const quantity = parseFloat(row.querySelector('[data-line-qty]').value);
        const price = parseFloat(row.querySelector('[data-line-price]').value);
        const unit = row.querySelector('[data-line-unit]').value.trim() || 'unit';
        const each = row.querySelector('[data-line-each]');
        if (price >= 0) total += price;
        if (each) each.textContent = quantity > 0 && price >= 0 ? `${money(price / quantity)} per ${unit}` : '';
      });
      if (totalEl) totalEl.textContent = money(total);
    };

    // Picking a saved item fills in its usual unit, unless a unit was typed by hand.
    const fillUnit = (row) => {
      const unit = row.querySelector('[data-line-unit]');
      const saved = usualUnits.get(key(row.querySelector('[data-line-item]').value));
      if (saved && (!unit.value || unit.dataset.autofilled === 'true')) {
        unit.value = saved;
        unit.dataset.autofilled = 'true';
      }
    };

    body.addEventListener('input', (event) => {
      const row = event.target.closest('[data-line]');
      if (!row) return;
      if (event.target.matches('[data-line-item]')) fillUnit(row);
      if (event.target.matches('[data-line-unit]')) event.target.dataset.autofilled = 'false';
      refresh();
    });

    // Rows are emptied and hidden rather than removed, so the field numbering stays continuous; empty rows are ignored.
    body.addEventListener('click', (event) => {
      const button = event.target.closest('[data-line-remove]');
      if (!button) return;
      const row = button.closest('[data-line]');
      row.querySelectorAll('input').forEach((input) => { input.value = ''; delete input.dataset.autofilled; });
      if (body.querySelectorAll('[data-line]:not([hidden])').length > 1) row.hidden = true;
      refresh();
    });

    const add = lines.querySelector('[data-line-add]');
    if (add) {
      add.addEventListener('click', () => {
        const rows = body.querySelectorAll('[data-line]');
        const index = rows.length;
        const copy = rows[rows.length - 1].cloneNode(true);
        copy.hidden = false;
        copy.querySelectorAll('input').forEach((input) => {
          input.value = '';
          delete input.dataset.autofilled;
          input.name = input.name.replace(/\[\d+\]/, `[${index}]`);
        });
        copy.querySelectorAll('[data-line-each]').forEach((each) => { each.textContent = ''; });
        body.appendChild(copy);
        copy.querySelector('[data-line-item]').focus();
      });
    }

    refresh();
  }

  // Receipt photos: show the chosen photos before they are uploaded.
  const receiptInput = document.querySelector('[data-receipt-input]');
  const receiptPreviews = document.querySelector('[data-receipt-previews]');
  if (receiptInput && receiptPreviews) {
    receiptInput.addEventListener('change', () => {
      receiptPreviews.replaceChildren();
      Array.from(receiptInput.files || []).forEach((file) => {
        const figure = document.createElement('figure');
        figure.className = 'receipt-thumb';
        const img = document.createElement('img');
        img.alt = '';
        img.src = URL.createObjectURL(file);
        const caption = document.createElement('figcaption');
        caption.textContent = `${file.name} (new)`;
        figure.append(img, caption);
        receiptPreviews.append(figure);
      });
    });
  }

  // Image preview before upload.
  const fileInput = document.querySelector('[data-image-input]');
  const image = document.querySelector('[data-image-preview]');
  if (fileInput && image) {
    fileInput.addEventListener('change', () => {
      const file = fileInput.files && fileInput.files[0];
      if (file) image.src = URL.createObjectURL(file);
    });
  }
})();
