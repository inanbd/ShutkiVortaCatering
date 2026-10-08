// Live per-delivery and weekly estimate for the restaurant standing-order form.
(() => {
  const form = document.getElementById('restaurant-order-form');
  if (!form) return;

  const num = (v) => { const n = parseFloat(v); return Number.isFinite(n) ? n : 0; };
  const round = (v) => Math.round((v + Number.EPSILON) * 100) / 100;
  const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
  const taxRate = num(form.dataset.taxRate);
  const fee = num(form.dataset.fee);
  const minSubtotal = num(form.dataset.minSubtotal);
  const minQty = num(form.dataset.minQty);
  const step = num(form.dataset.step);
  const $ = (sel) => form.querySelector(sel);

  const update = () => {
    let subtotal = 0;
    let pounds = 0;
    form.querySelectorAll('[data-item]').forEach((row) => {
      const input = row.querySelector('input[type=number]');
      const qty = num(input.value);
      const line = round(qty * num(row.dataset.price));
      const tooLow = qty > 0 && qty < minQty;
      const offStep = qty > 0 && step > 0 && Math.abs(((qty - minQty) / step) - Math.round((qty - minQty) / step)) > 1e-9;
      row.classList.toggle('is-selected', qty > 0);
      row.classList.toggle('is-invalid', tooLow || offStep);
      input.setCustomValidity(tooLow ? `Minimum ${minQty} lb` : offStep ? `Use ${step} lb steps` : '');
      row.querySelector('[data-line-total]').textContent = qty > 0 ? money.format(line) : '—';
      subtotal += line;
      pounds += qty;
    });

    const delivery = form.querySelector('input[data-fulfillment]:checked')?.value === 'Delivery';
    const deliveryFee = delivery && subtotal > 0 ? fee : 0;
    // Mirrors the server: delivery fee is taxable; restaurants with a resale certificate are exempted later by our team.
    const tax = round((subtotal + deliveryFee) * taxRate);
    const total = subtotal + deliveryFee + tax;
    const days = form.querySelectorAll('input[data-day]:checked').length;

    $('[data-sum-qty]').textContent = `${Math.round(pounds * 100) / 100} lb`;
    $('[data-sum-subtotal]').textContent = money.format(subtotal);
    $('[data-sum-fee]').textContent = money.format(delivery ? fee : 0);
    $('[data-sum-fee-row]').classList.toggle('hidden', !delivery);
    $('[data-sum-tax]').textContent = money.format(tax);
    $('[data-sum-total]').textContent = money.format(total);
    $('[data-sum-week]').textContent = days > 0 && subtotal > 0
      ? `${days} deliver${days === 1 ? 'y' : 'ies'} a week ≈ ${money.format(total * days)} per week.`
      : 'Choose your vortas and delivery days to see a weekly estimate.';
    $('[data-sum-minimum]').classList.toggle('hidden', subtotal === 0 || subtotal >= minSubtotal);

    form.querySelector('[data-delivery-fields]')?.classList.toggle('hidden', !delivery);
  };

  form.addEventListener('input', update);
  form.addEventListener('change', update);
  update();
})();
