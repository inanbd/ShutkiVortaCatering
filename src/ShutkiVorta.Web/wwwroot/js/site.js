// Shutki Vorta Catering — progressive enhancements (the site works without JavaScript).
(function () {
  'use strict';

  const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

  // Mobile navigation
  const toggle = document.querySelector('.nav-toggle');
  const nav = document.getElementById('main-nav');
  if (toggle && nav) {
    toggle.addEventListener('click', () => {
      const open = nav.classList.toggle('open');
      toggle.setAttribute('aria-expanded', String(open));
    });
  }

  // Quantity steppers (− / +) honouring min and step; updates the live total on item pages.
  document.querySelectorAll('[data-qty]').forEach((wrapper) => {
    const input = wrapper.querySelector('input');
    if (!input) return;
    const step = parseFloat(input.step) || 0.5;
    const min = parseFloat(input.min) || step;
    const max = parseFloat(input.max) || 200;
    const form = wrapper.closest('form');

    const updateTotal = () => {
      const price = form && parseFloat(form.dataset.price);
      const target = form && form.querySelector('[data-line-total]');
      const qty = parseFloat(input.value);
      if (target && price && qty > 0) target.textContent = money.format(price * qty);
    };

    wrapper.querySelectorAll('[data-qty-step]').forEach((button) => {
      button.addEventListener('click', () => {
        const direction = parseInt(button.dataset.qtyStep, 10);
        let value = (parseFloat(input.value) || min) + direction * step;
        value = Math.min(max, Math.max(min, Math.round(value / step) * step));
        input.value = String(Number(value.toFixed(2)));
        input.dispatchEvent(new Event('change', { bubbles: true }));
      });
    });

    input.addEventListener('input', updateTotal);
    input.addEventListener('change', updateTotal);
  });

  // Cart: save quantity changes automatically.
  document.querySelectorAll('form[data-autosubmit]').forEach((form) => {
    let timer;
    form.querySelectorAll('input[name="quantity"]').forEach((input) => {
      input.addEventListener('change', () => {
        clearTimeout(timer);
        timer = setTimeout(() => form.requestSubmit ? form.requestSubmit() : form.submit(), 600);
      });
    });
  });

  // Confirmation prompts for destructive actions.
  document.querySelectorAll('form[data-confirm]').forEach((form) => {
    form.addEventListener('submit', (event) => {
      if (!window.confirm(form.dataset.confirm)) event.preventDefault();
    });
  });

  // Checkout: pickup/delivery switch and date → time slot filtering.
  const checkout = document.getElementById('checkout-form');
  if (checkout) {
    const deliveryFields = checkout.querySelector('[data-delivery-fields]');
    const deliveryRow = checkout.querySelector('[data-delivery-row]');
    const totals = checkout.querySelector('table.totals');
    const taxCell = checkout.querySelector('[data-tax]');
    const totalCell = checkout.querySelector('[data-total]');

    const applyFulfillment = () => {
      const selected = checkout.querySelector('input[data-fulfillment]:checked');
      const isDelivery = selected && selected.value === 'Delivery';
      if (deliveryFields) deliveryFields.classList.toggle('hidden', !isDelivery);
      if (deliveryRow) deliveryRow.classList.toggle('hidden', !isDelivery);
      if (totals && taxCell && totalCell) {
        taxCell.textContent = isDelivery ? totals.dataset.deliveryTax : totals.dataset.pickupTax;
        totalCell.textContent = isDelivery ? totals.dataset.deliveryTotal : totals.dataset.pickupTotal;
      }
    };
    checkout.querySelectorAll('input[data-fulfillment]').forEach((radio) => radio.addEventListener('change', applyFulfillment));
    applyFulfillment();

    const slotData = document.getElementById('slot-data');
    const dateSelect = checkout.querySelector('[data-date-select]');
    const timeSelect = checkout.querySelector('[data-time-select]');
    if (slotData && dateSelect && timeSelect) {
      const slots = JSON.parse(slotData.textContent || '{}');
      dateSelect.addEventListener('change', () => {
        const previous = timeSelect.value;
        timeSelect.innerHTML = '';
        (slots[dateSelect.value] || []).forEach((slot) => {
          const option = new Option(slot.label, slot.value, false, slot.value === previous);
          timeSelect.add(option);
        });
      });
    }

    // Prevent double submissions.
    checkout.addEventListener('submit', () => {
      const button = checkout.querySelector('button[type="submit"]');
      if (window.jQuery && window.jQuery(checkout).valid && !window.jQuery(checkout).valid()) return;
      if (button) setTimeout(() => { button.disabled = true; button.textContent = 'Placing your order…'; }, 0);
    });
  }
})();
