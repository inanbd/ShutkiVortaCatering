// Admin panel helpers.
(function () {
  'use strict';

  document.querySelectorAll('form[data-confirm]').forEach((form) => {
    form.addEventListener('submit', (event) => {
      if (!window.confirm(form.dataset.confirm)) event.preventDefault();
    });
  });

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
