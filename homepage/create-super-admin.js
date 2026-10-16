(function () {
  "use strict";
  const form = document.querySelector("#superAdminSetupForm");
  const message = document.querySelector("#setupMessage");
  const submit = document.querySelector("#setupSubmit");
  const passwordPolicy = "Password must contain at least 12 characters, including uppercase, lowercase, a number, and a symbol.";
  const field = id => document.querySelector(id).value.trim();
  const showError = text => { message.textContent = text; message.classList.add("error"); };

  async function csrf() {
    const response = await fetch("/api/auth/csrf", { credentials: "include" });
    if (!response.ok) throw new Error("Unable to start the secure setup service.");
    return (await response.json()).token;
  }
  async function status() {
    const response = await fetch("/api/setup/status", { credentials: "include", cache: "no-store" });
    if (!response.ok) throw new Error("Unable to check system setup status.");
    return response.json();
  }
  async function start() {
    try { if (!(await status()).requiresSetup) location.replace("admin-login.html"); }
    catch (error) { showError(error.message); submit.disabled = true; }
  }
  form.addEventListener("submit", async event => {
    event.preventDefault();
    const password = document.querySelector("#setupPassword").value;
    const confirmPassword = document.querySelector("#setupConfirmPassword").value;
    if (!(password.length >= 12 && /[A-Z]/.test(password) && /[a-z]/.test(password) && /\d/.test(password) && /[^A-Za-z0-9]/.test(password))) { showError(passwordPolicy); return; }
    if (password !== confirmPassword) { showError("Passwords do not match."); return; }
    submit.disabled = true; submit.textContent = "Creating secure account..."; message.textContent = "";
    try {
      const response = await fetch("/api/setup/super-admin", { method: "POST", credentials: "include", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": await csrf() }, body: JSON.stringify({ firstName: field("#setupFirstName"), lastName: field("#setupLastName"), administratorId: field("#setupAdministratorId") || null, email: field("#setupEmail"), phoneNumber: field("#setupPhone"), password, confirmPassword }) });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.message || body.detail || Object.values(body.errors || {}).flat().join(" ") || "Unable to create the Super Administrator account.");
      sessionStorage.setItem("authMessage", "Super Administrator account created successfully.");
      const toast = document.createElement("div"); toast.className = "toast success"; toast.textContent = "Super Administrator account created successfully."; document.querySelector("#setupToast").appendChild(toast);
      setTimeout(() => location.replace("admin-login.html?role=super_admin"), 1800);
    } catch (error) { showError(error.message); submit.disabled = false; submit.textContent = "Create Super Administrator"; }
  });
  start();
})();
