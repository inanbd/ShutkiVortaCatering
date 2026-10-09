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
