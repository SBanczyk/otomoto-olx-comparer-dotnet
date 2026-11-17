(function () {
  // Scraping takes a few seconds: show a spinner on the submit button and block double submits.
  document.querySelectorAll('form[data-busy-form]').forEach(function (form) {
    form.addEventListener('submit', function (event) {
      var button = event.submitter;
      if (!button || button.hasAttribute('data-no-busy')) { return; }
      if (form.dataset.submitting === 'true') { event.preventDefault(); return; }
      form.dataset.submitting = 'true';
      var text = button.getAttribute('data-busy-text');
      if (text) { button.textContent = text; }
      button.classList.add('btn-busy');
    });
  });

  // Confirmation without inline handlers.
  document.querySelectorAll('form[data-confirm]').forEach(function (form) {
    form.addEventListener('submit', function (event) {
      if (!window.confirm(form.getAttribute('data-confirm'))) { event.preventDefault(); }
    });
  });

  document.querySelectorAll('[data-copy-link]').forEach(function (button) {
    button.addEventListener('click', function () {
      var done = function () {
        var original = button.textContent;
        button.textContent = 'Skopiowano ✓';
        setTimeout(function () { button.textContent = original; }, 1800);
      };
      if (navigator.clipboard) { navigator.clipboard.writeText(window.location.href).then(done); }
    });
  });
})();
