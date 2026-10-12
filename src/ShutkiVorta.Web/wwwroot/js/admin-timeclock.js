// Time clock: says beforehand whether the location will be recorded, asks the device for it when someone clocks in or out
// (or an admin sets the kitchen location), then posts the form with the result in its hidden fields.
(function () {
  'use strict';

  const geolocation = 'geolocation' in navigator ? navigator.geolocation : null;
  const FIRST_TRY = { enableHighAccuracy: true, timeout: 15000, maximumAge: 0 };
  // After a timeout: once more, less precise, and a position up to 2 minutes old will do.
  const RETRY = { enableHighAccuracy: false, timeout: 15000, maximumAge: 120000 };

  // Resolves with { status, latitude, longitude, accuracy }; status is a LocationStatus name. Never rejects.
  function locate() {
    return new Promise((resolve) => {
      if (!window.isSecureContext) {
        resolve({ status: 'InsecureConnection' });
        return;
      }

      if (!geolocation) {
        resolve({ status: 'NotSupported' });
        return;
      }

      const found = (position) => resolve({
        status: 'Captured',
        latitude: position.coords.latitude,
        longitude: position.coords.longitude,
        accuracy: position.coords.accuracy,
      });
      const failed = (error, retried) => {
        if (error.code === 3 && !retried) {
          geolocation.getCurrentPosition(found, (again) => failed(again, true), RETRY);
          return;
        }

        resolve({ status: error.code === 1 ? 'PermissionDenied' : error.code === 3 ? 'Timeout' : 'Unavailable' });
      };
      geolocation.getCurrentPosition(found, (error) => failed(error, false), FIRST_TRY);
    });
  }

  function fill(form, result) {
    const set = (name, value) => {
      const input = form.querySelector(`[data-punch="${name}"]`);
      if (input) input.value = value === undefined || value === null ? '' : String(value);
    };
    set('status', result.status);
    set('latitude', result.latitude);
    set('longitude', result.longitude);
    set('accuracy', result.accuracy);
  }

  // The form's own action (…?handler=ClockIn), without firing the submit event again.
  const post = (form) => HTMLFormElement.prototype.submit.call(form);

  // ---- Before the tap: will the location be recorded? ----
  const statusLine = document.querySelector('[data-location-status]');
  const help = document.querySelector('[data-location-help]');
  const helpLink = statusLine ? statusLine.querySelector('[data-help-link]') : null;

  function show(state, text, offerHelp) {
    statusLine.dataset.state = state;
    statusLine.querySelector('[data-location-text]').textContent = text;
    if (helpLink) helpLink.hidden = !offerHelp;
  }

  async function describeLocation() {
    if (!statusLine) return;
    if (!window.isSecureContext) {
      show('warn', 'Location needs a secure (https) connection, so it won\'t be recorded.', false);
      return;
    }

    if (!geolocation) {
      show('warn', 'This browser can\'t share its location, so it won\'t be recorded.', false);
      return;
    }

    let state = 'prompt';
    try {
      if (navigator.permissions && navigator.permissions.query) {
        const permission = await navigator.permissions.query({ name: 'geolocation' });
        state = permission.state;
        permission.onchange = describeLocation;
      }
    } catch {
      // Some browsers can't report it; the device will simply ask.
    }

    if (state === 'granted') show('ok', 'Location will be recorded.', false);
    else if (state === 'denied') show('warn', 'Location is blocked on this phone –', true);
    else show('info', 'Your phone will ask to share your location: tap Allow.', false);
  }

  if (helpLink && help) {
    helpLink.addEventListener('click', () => { help.open = true; });
  }

  describeLocation();

  // ---- Clock in / clock out ----
  document.querySelectorAll('form[data-clock-form]').forEach((form) => {
    const button = form.querySelector('[data-clock-button]');
    const locating = form.querySelector('[data-locating]');
    const skip = form.querySelector('[data-skip-location]');
    let sent = false;

    const send = (result) => {
      if (sent) return;
      sent = true;
      fill(form, result);
      post(form);
    };

    form.addEventListener('submit', (event) => {
      event.preventDefault();
      if (sent || button.disabled) return;
      button.disabled = true;
      if (locating) locating.hidden = false;
      locate().then(send);
    });

    if (skip) skip.addEventListener('click', () => send({ status: 'NotProvided' }));
  });

  // ---- Admins: the kitchen is where I am now ----
  document.querySelectorAll('form[data-kitchen-form]').forEach((form) => {
    const button = form.querySelector('[data-kitchen-button]');
    const message = form.querySelector('[data-kitchen-message]');
    const say = (text) => { if (message) message.textContent = text; };

    form.addEventListener('submit', (event) => {
      if (event.defaultPrevented) return; // "Cancel" in the confirmation (admin.js)
      event.preventDefault();
      button.disabled = true;
      say('Getting your location…');
      locate().then((result) => {
        if (result.status !== 'Captured') {
          button.disabled = false;
          const blocked = result.status === 'PermissionDenied';
          say(blocked
            ? 'Location is blocked for this site. See "How to turn location on" below, then try again.'
            : 'Your location could not be found. Try again (outside or near a window helps).');
          if (blocked && help) help.open = true;
          return;
        }

        fill(form, result);
        post(form);
      });
    });
  });

  // A page restored from the back/forward cache after posting would show an old state and a disabled button.
  window.addEventListener('pageshow', (event) => {
    if (event.persisted && document.querySelector('form[data-clock-form], form[data-kitchen-form]')) window.location.reload();
  });
})();
