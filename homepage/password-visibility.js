(function () {
  "use strict";

  const icons = `
    <svg class="password-eye-open" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path d="M12 5C6.5 5 2.5 10.3 2 12c.5 1.7 4.5 7 10 7s9.5-5.3 10-7c-.5-1.7-4.5-7-10-7Zm0 11a4 4 0 1 1 0-8 4 4 0 0 1 0 8Zm0-2a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z"/>
    </svg>
    <svg class="password-eye-closed" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path d="m3.3 2 18.4 18.4-1.4 1.4-3-3A12.4 12.4 0 0 1 12 20C5 20 2 12 2 12a17 17 0 0 1 4-5.1L1.9 3.4 3.3 2Zm5.1 6.3A10.8 10.8 0 0 0 4.3 12c.8 1.7 3.3 6 7.7 6 1.3 0 2.4-.4 3.4-1l-2-2a3.4 3.4 0 0 1-1.4.3A3.4 3.4 0 0 1 8.6 12c0-.5.1-1 .3-1.4l-.5-.5Zm3.6-4.2c7 0 10 7.9 10 7.9a16.4 16.4 0 0 1-2.7 4.1l-1.4-1.4a12.4 12.4 0 0 0 1.8-2.7c-.8-1.7-3.3-6-7.7-6-.8 0-1.5.1-2.2.4L8.2 4.8c1.1-.5 2.3-.7 3.8-.7Z"/>
    </svg>`;

  function bind(input, index) {
    if (input.dataset.visibilityBound === "true") return;
    if (!input.id) input.id = `passwordField${index + 1}`;

    let toggle = input.parentElement?.querySelector(":scope > .password-toggle, :scope > .register-password-toggle, :scope > .password-visibility-toggle");
    let wrapper = input.parentElement;
    if (!toggle) {
      wrapper = document.createElement("span");
      input.parentNode.insertBefore(wrapper, input);
      wrapper.appendChild(input);
      toggle = document.createElement("button");
      wrapper.appendChild(toggle);
    }

    wrapper.classList.add("password-visibility-wrap");
    toggle.type = "button";
    toggle.className = "password-visibility-toggle";
    toggle.setAttribute("aria-label", "Show password");
    toggle.setAttribute("aria-controls", input.id);
    toggle.setAttribute("aria-pressed", "false");
    toggle.innerHTML = icons;
    input.dataset.visibilityBound = "true";

    toggle.addEventListener("mousedown", event => event.preventDefault());
    toggle.addEventListener("click", () => {
      const selectionStart = input.selectionStart;
      const selectionEnd = input.selectionEnd;
      const visible = input.type === "password";
      input.type = visible ? "text" : "password";
      toggle.classList.toggle("is-visible", visible);
      toggle.setAttribute("aria-label", visible ? "Hide password" : "Show password");
      toggle.setAttribute("aria-pressed", String(visible));
      input.focus({ preventScroll: true });
      if (selectionStart !== null && selectionEnd !== null) input.setSelectionRange(selectionStart, selectionEnd);
    });
  }

  function initialise(root = document) {
    root.querySelectorAll('input[type="password"]').forEach(bind);
  }

  window.UMATPasswordVisibility = { initialise };
  initialise();
  new MutationObserver(records => {
    if (records.some(record => record.addedNodes.length)) initialise();
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
