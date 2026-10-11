// Users and Permissions pages: the invitation link (copy, share), the invitation email's status, permission forms.
(function () {
  'use strict';

  // ---- The invitation link, shown once: copy it, or share it (text message, WhatsApp…) ----
  const linkInput = document.querySelector('[data-invite-link]');
  const copyStatus = document.querySelector('[data-copy-status]');
  const say = (text) => { if (copyStatus) copyStatus.textContent = text; };

  const copyButton = document.querySelector('[data-copy-link]');
  if (linkInput && copyButton) {
    copyButton.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(linkInput.value);
        say('Copied. Paste it into a text message.');
      } catch {
        // No clipboard access (e.g. not https): select it so a long-press or Ctrl+C copies it.
        linkInput.focus();
        linkInput.select();
        say('Selected: copy it with Ctrl+C, or long-press on a phone.');
      }
    });
  }

  // Without the share sheet the link stays an sms: link, which opens the phone's messages app.
  const shareLink = document.querySelector('[data-share-link]');
  if (shareLink && navigator.share) {
    shareLink.addEventListener('click', async (event) => {
      event.preventDefault();
      try {
        await navigator.share({ title: 'Your staff invitation', text: shareLink.dataset.shareText });
      } catch (error) {
        if (error && error.name !== 'AbortError') window.location.href = shareLink.href;
      }
    });
  }

  // ---- The invitation email is sent in the background: look again while it is still queued ----
  const status = document.querySelector('[data-email-status][data-waiting="true"]');
  if (status && status.dataset.url) {
    let tries = 0;
    const check = async () => {
      tries += 1;
      try {
        const response = await fetch(status.dataset.url, { headers: { Accept: 'application/json' }, credentials: 'same-origin' });
        if (response.ok) {
          const email = await response.json();
          status.textContent = email.text;
          status.className = `pill ${email.css}`;
          if (!email.waiting) return;
        }
      } catch {
        // Try again below.
      }

      if (tries < 12) window.setTimeout(check, 2500);
    };
    window.setTimeout(check, 1500);
  }

  // ---- A person's permissions: the module selects only matter for "Custom" ----
  const permissionsForm = document.querySelector('[data-permissions-form]');
  if (permissionsForm) {
    const levels = permissionsForm.querySelector('[data-custom-levels]');
    const sync = () => {
      const custom = permissionsForm.querySelector('[data-mode][value="custom"]').checked;
      levels.toggleAttribute('data-disabled', !custom);
      levels.querySelectorAll('select').forEach((select) => { select.disabled = !custom; });
    };
    permissionsForm.querySelectorAll('[data-mode]').forEach((radio) => radio.addEventListener('change', sync));
    sync();
  }

  // ---- Changing a role: the confirmation names the new role ----
  const roleForm = document.querySelector('form[data-role-form]');
  if (roleForm) {
    const select = roleForm.querySelector('select[name="role"]');
    const update = () => {
      const role = select.selectedOptions[0].textContent.trim();
      const reset = roleForm.dataset.custom === 'true'
        ? ` Their custom permissions will be reset to the ${role} permissions.`
        : ` They get the ${role} permissions.`;
      roleForm.dataset.confirm = `Make ${roleForm.dataset.name} ${/^[AEIOU]/.test(role) ? 'an' : 'a'} ${role}?${reset}`;
    };
    select.addEventListener('change', update);
    update();
  }

  // ---- Permissions page: show which role has unsaved changes, and warn before leaving with them ----
  const roleSelects = document.querySelectorAll('.permissions-table select[data-role]');
  if (roleSelects.length > 0) {
    let submitting = false;
    const changed = () => Array.from(roleSelects).filter((select) => !select.selectedOptions[0].defaultSelected);
    const refresh = () => {
      const dirty = new Set(changed().map((select) => select.dataset.role));
      roleSelects.forEach((select) => select.toggleAttribute('data-changed', !select.selectedOptions[0].defaultSelected));
      document.querySelectorAll('[data-unsaved]').forEach((note) => { note.hidden = !dirty.has(note.dataset.unsaved); });
    };

    roleSelects.forEach((select) => {
      select.addEventListener('change', () => {
        const help = select.parentElement.querySelector('.level-help');
        if (help) help.textContent = select.selectedOptions[0].title || '';
        refresh();
      });
    });

    document.querySelectorAll('form[data-role-save]').forEach((form) => form.addEventListener('submit', () => { submitting = true; }));
    window.addEventListener('beforeunload', (event) => {
      if (submitting || changed().length === 0) return;
      event.preventDefault();
      event.returnValue = '';
    });
  }
})();
