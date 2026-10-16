const slides = document.querySelectorAll(".slide");
const menuToggle = document.querySelector(".menu-toggle");
const navMenu = document.querySelector("#navMenu");
const loginForm = document.querySelector(".login-card");
const studentLoginForm = document.querySelector("#studentLoginForm");
const studentLoginIdentity = document.querySelector("#studentLoginIdentity");
const homepageLoginIdentity = document.querySelector("#homepageLoginIdentity");
const umatStudentEmailPattern = /^(?:MA|CE|GL|GM|CV|MC|EL|DS|ES|TP)-[A-Z0-9._%+-]+@st\.umat\.edu\.gh$/i;
const umatReferenceNumberPattern = /^901\d{5}(\d{2})$/;
const studentAccountsStorageKey = "umatStudentAccounts";
const currentStudentStorageKey = "umatCurrentStudent";
const serviceHealthRetryDelayMs = 750;
const serviceHealthPollIntervalMs = 10000;
let runtimeConfigPromise;

function applyRegistrationLoginHandoff() {
  const parameters = new URLSearchParams(window.location.search);
  const identifier = parameters.get("identifier");
  const identityInput = studentLoginIdentity || homepageLoginIdentity;
  if (!identifier || !identityInput) return;

  identityInput.value = identifier;
  if (parameters.get("registered") === "1") {
    identityInput.focus();
    const form = identityInput.closest("form");
    const status = form ? serviceStatusElement(form) : null;
    if (status) {
      status.textContent = "Account created successfully. Enter your password to continue.";
      status.classList.add("is-ready");
      status.classList.remove("is-error");
    }
  }
}

if (window.location.protocol === "file:") {
  const requestedPage = window.location.pathname.split("/").pop() || "homepage.html";
  const safePage = /^[a-z0-9-]+\.html$/i.test(requestedPage) ? requestedPage : "homepage.html";
  window.location.replace(`http://localhost:8080/${safePage}${window.location.search}${window.location.hash}`);
}

function delay(milliseconds) {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds));
}

async function loadRuntimeConfig() {
  if (window.location.protocol === "file:") {
    return new Promise(() => {});
  }
  if (!runtimeConfigPromise) {
    runtimeConfigPromise = fetch("/runtime-config.json", { cache: "no-store" })
      .then((response) => response.ok ? response.json() : {})
      .catch(() => ({}));
  }
  return runtimeConfigPromise;
}

async function apiUrl(path) {
  const config = await loadRuntimeConfig();
  const baseUrl = String(config.apiBaseUrl || window.location.origin).replace(/\/$/, "");
  return `${baseUrl}${path}`;
}

async function fetchApi(path, options = {}, attempts = 1) {
  const url = await apiUrl(path);
  let lastError;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      const response = await fetch(url, { credentials: "include", ...options });
      if (![502, 503, 504].includes(response.status) || attempt === attempts) return response;
    } catch (error) {
      lastError = error;
      if (attempt === attempts) throw error;
    }
    await delay(serviceHealthRetryDelayMs * attempt);
  }
  throw lastError || new Error("Unable to reach the API.");
}

async function getCsrfToken() {
  const response = await fetchApi("/api/auth/csrf", { cache: "no-store" }, 3);
  if (!response.ok) {
    if (response.status === 429) throw new Error("Too many secure requests. Please wait a moment and try again.");
    throw new Error("Unable to start a secure request. Please refresh the page and try again.");
  }
  return (await response.json()).token;
}

async function postApi(path, body) {
  const token = await getCsrfToken();
  return fetchApi(path, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
    body: JSON.stringify(body)
  }, 3);
}

function loginForms() {
  return [loginForm, studentLoginForm].filter(Boolean);
}

function serviceStatusElement(form) {
  let status = form.querySelector("[data-login-service-status]");
  if (!status) {
    status = document.createElement("p");
    status.dataset.loginServiceStatus = "";
    status.className = "login-service-status";
    status.setAttribute("role", "status");
    const button = form.querySelector('button[type="submit"]');
    button?.insertAdjacentElement("afterend", status);
  }
  return status;
}

function setLoginAvailability(available, message = "") {
  loginForms().forEach((form) => {
    const button = form.querySelector('button[type="submit"]');
    if (button) button.disabled = !available;
    const status = serviceStatusElement(form);
    status.textContent = message;
    status.classList.toggle("is-error", !available && Boolean(message));
    status.classList.toggle("is-ready", available && Boolean(message));
  });
}

async function checkRequiredLoginServices(showReadyMessage = false) {
  setLoginAvailability(false, "Checking secure login services...");
  try {
    const backendResponse = await fetchApi("/api/health", { cache: "no-store" }, 3);
    if (!backendResponse.ok) {
      const databaseResponse = await fetchApi("/api/database/health", { cache: "no-store" }, 2);
      if (!databaseResponse.ok) {
        setLoginAvailability(false, "Database connection failed.");
        return false;
      }
      const authenticationResponse = await fetchApi("/api/auth/health", { cache: "no-store" }, 2);
      if (!authenticationResponse.ok) {
        setLoginAvailability(false, "Authentication service is temporarily unavailable.");
        return false;
      }
      setLoginAvailability(false, "Backend server is not ready.");
      return false;
    }

    const [databaseResponse, authenticationResponse] = await Promise.all([
      fetchApi("/api/database/health", { cache: "no-store" }, 2),
      fetchApi("/api/auth/health", { cache: "no-store" }, 2)
    ]);
    if (!databaseResponse.ok) {
      setLoginAvailability(false, "Database connection failed.");
      return false;
    }
    if (!authenticationResponse.ok) {
      setLoginAvailability(false, "Authentication service is temporarily unavailable.");
      return false;
    }
    setLoginAvailability(true, showReadyMessage ? "Secure login service is ready." : "");
    return true;
  } catch (error) {
    setLoginAvailability(false, "Unable to connect to the login API.");
    return false;
  }
}

async function readApiError(response, fallback) {
  try {
    const problem = await response.json();
    if (problem.errors && typeof problem.errors === "object") {
      const messages = Object.values(problem.errors).flat().filter(Boolean);
      if (messages.length) return messages[0];
    }
    return problem.detail || problem.title || fallback;
  } catch {
    return fallback;
  }
}

function readWindowStudentState() {
  try {
    const state = JSON.parse(window.name || "{}");
    return state.umatComplaintSystem || {};
  } catch {
    return {};
  }
}

function writeWindowStudentState(updates) {
  const state = readWindowStudentState();
  window.name = JSON.stringify({ umatComplaintSystem: { ...state, ...updates } });
}

function readStudentAccounts() {
  const windowAccounts = readWindowStudentState().accounts;
  if (Array.isArray(windowAccounts) && windowAccounts.length) return windowAccounts;

  try {
    const accounts = JSON.parse(localStorage.getItem(studentAccountsStorageKey) || "[]");
    if (Array.isArray(accounts) && accounts.length) return accounts;
  } catch {
    // The window state below keeps the static file version working when storage is restricted.
  }
  return [];
}

function storeStudentAccounts(accounts) {
  try {
    localStorage.setItem(studentAccountsStorageKey, JSON.stringify(accounts));
  } catch {
    // Continue with the same-tab window state fallback.
  }
  writeWindowStudentState({ accounts });
}

function storeCurrentStudent(student) {
  try {
    localStorage.setItem(currentStudentStorageKey, JSON.stringify(student));
  } catch {
    // Continue with the same-tab window state fallback.
  }
  writeWindowStudentState({ currentStudent: student });
}

function importTransferredStudentProfile() {
  const profileData = new URLSearchParams(location.search).get("profile");
  if (!profileData) return;

  try {
    const student = JSON.parse(profileData);
    const accounts = readStudentAccounts();
    const existingIndex = accounts.findIndex((account) =>
      (account.referenceNumber || "").toLowerCase() === (student.referenceNumber || "").toLowerCase()
    );

    if (existingIndex >= 0) accounts[existingIndex] = student;
    else accounts.push(student);
    storeStudentAccounts(accounts);
    writeWindowStudentState({ lastRegisteredStudent: student });

    try {
      history.replaceState(null, "", `${location.pathname}${location.hash}`);
    } catch {
      // Keeping the profile in the local URL is harmless when file history cannot be changed.
    }
  } catch {
    // Ignore invalid profile data and continue with normal login.
  }
}

importTransferredStudentProfile();

function startStudentSession(identity) {
  const normalizedIdentity = identity.trim().toLowerCase();
  const accounts = readStudentAccounts();
  let registeredStudent = accounts.find((account) =>
    account.referenceNumber?.toLowerCase() === normalizedIdentity ||
    account.studentEmail?.toLowerCase() === normalizedIdentity
  );

  const isEmail = normalizedIdentity.includes("@");
  if (!registeredStudent && accounts.length === 1) {
    registeredStudent = {
      ...accounts[0],
      studentEmail: isEmail ? identity.trim() : accounts[0].studentEmail
    };
  }

  const student = registeredStudent || {
    fullName: "Student",
    referenceNumber: isEmail ? "" : identity.trim(),
    studentEmail: isEmail ? identity.trim() : "",
    dateOfBirth: "",
    department: "",
    departmentName: ""
  };

  storeCurrentStudent(student);
  return student;
}

function openStudentDashboard(student) {
  const session = encodeURIComponent(JSON.stringify(student));
  window.location.href = `student-dashboard.html#session=${session}`;
}

function validateStudentIdentity(input) {
  if (!input) return true;
  const value = input.value.trim();
  const match = value.match(umatReferenceNumberPattern);
  const isValidReference = Boolean(match && Number(match[1]) >= 22);
  const isValid = value.includes("@") ? umatStudentEmailPattern.test(value) : isValidReference;
  input.setCustomValidity(isValid ? "" : "Invalid student email, reference number, or password.");
  if (!isValid) input.reportValidity();
  return isValid;
}

function validateRegistrationReference(input) {
  if (!input) return false;
  const match = input.value.trim().match(umatReferenceNumberPattern);
  const isValid = Boolean(match && Number(match[1]) >= 22);
  input.setCustomValidity(isValid ? "" : "Invalid reference number.");
  if (!isValid) input.reportValidity();
  return isValid;
}

let activeSlide = 0;

function showSlide(index) {
  slides.forEach((slide, slideIndex) => {
    slide.classList.toggle("active", slideIndex === index);
  });
}

if (slides.length > 1) {
  window.setInterval(() => {
    activeSlide = (activeSlide + 1) % slides.length;
    showSlide(activeSlide);
  }, 4800);
}

if (menuToggle && navMenu) {
  menuToggle.addEventListener("click", () => {
  const isOpen = navMenu.classList.toggle("open");
  menuToggle.setAttribute("aria-expanded", String(isOpen));
  });
}

async function submitStudentLogin(form, identityInput) {
  if (!validateStudentIdentity(identityInput)) return;
  if (!await checkRequiredLoginServices()) return;
  const password = form.elements.password?.value || "";
  const rememberMe = form.querySelector('input[type="checkbox"]')?.checked || false;
  const button = form.querySelector('button[type="submit"]');
  button?.setAttribute("disabled", "");
  try {
    const response = await postApi("/api/auth/student/login", {
      identifier: identityInput.value.trim(), password, rememberMe
    });
    if (!response.ok) {
      identityInput.setCustomValidity(await readApiError(response, "Invalid student email, reference number, or password."));
      identityInput.reportValidity();
      identityInput.addEventListener("input", () => identityInput.setCustomValidity(""), { once: true });
      return;
    }
    const result = await response.json();
    if (!result.authenticated) {
      identityInput.setCustomValidity(result.requiresEmailVerification
        ? "Verify your student email before logging in."
        : "Invalid student email, reference number, or password.");
      identityInput.reportValidity();
      return;
    }
    const returnUrl = new URLSearchParams(window.location.search).get("returnUrl");
    window.location.href = returnUrl === "submit-complaint.html" ? returnUrl : "student-dashboard.html";
  } catch {
    identityInput.setCustomValidity("Unable to connect to the login API.");
    identityInput.reportValidity();
  } finally {
    await checkRequiredLoginServices();
  }
}

const institutionalLoginButton = document.querySelector("#institutionalLoginButton");
const institutionalLoginMessage = document.querySelector("#institutionalLoginMessage");

function loadGoogleIdentityServices() {
  if (window.google?.accounts?.id) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "https://accounts.google.com/gsi/client";
    script.async = true;
    script.defer = true;
    script.onload = resolve;
    script.onerror = reject;
    document.head.appendChild(script);
  });
}

async function completeInstitutionalLogin(credential) {
  institutionalLoginButton.disabled = true;
  institutionalLoginMessage.textContent = "Verifying your UMaT student account...";
  try {
    const response = await postApi("/api/auth/student/institutional", { credential });
    if (!response.ok) {
      institutionalLoginMessage.textContent = await readApiError(response,
        "UMaT student email authentication was unsuccessful. Please try again.");
      return;
    }
    const result = await response.json();
    if (result.showWelcomeMessage) {
      sessionStorage.setItem("umatAccountCreatedWelcome",
        `Account created successfully. Welcome, ${result.user?.firstName || "Student"}.`);
    }
    window.location.href = "student-dashboard.html";
  } catch {
    institutionalLoginMessage.textContent =
      "UMaT student email authentication was unsuccessful. Please try again.";
  } finally {
    institutionalLoginButton.disabled = false;
  }
}

async function initializeInstitutionalLogin() {
  if (!institutionalLoginButton) return;
  try {
    const response = await fetchApi("/api/auth/student/institutional/config", { cache: "no-store" });
    if (!response.ok) throw new Error();
    const config = await response.json();
    if (!config.enabled || config.provider !== "Google") {
      institutionalLoginButton.disabled = true;
      institutionalLoginMessage.textContent = "UMaT student email login is not configured.";
      return;
    }
    await loadGoogleIdentityServices();
    google.accounts.id.initialize({
      client_id: config.clientId,
      hd: config.hostedDomain,
      auto_select: false,
      cancel_on_tap_outside: true,
      callback: response => completeInstitutionalLogin(response.credential)
    });
    institutionalLoginButton.addEventListener("click", () => {
      institutionalLoginMessage.textContent = "";
      google.accounts.id.prompt(notification => {
        if (notification.isSkippedMoment?.() || notification.isDismissedMoment?.()) {
          institutionalLoginMessage.textContent = "";
        } else if (notification.isNotDisplayed?.()) {
          institutionalLoginMessage.textContent =
            "UMaT student email authentication was unsuccessful. Please try again.";
        }
      });
    });
  } catch {
    institutionalLoginButton.disabled = true;
    institutionalLoginMessage.textContent = "UMaT student email login is temporarily unavailable.";
  }
}

if (loginForm) {
  loginForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    await submitStudentLogin(loginForm, homepageLoginIdentity);
  });
}

if (studentLoginForm) {
  studentLoginForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    await submitStudentLogin(studentLoginForm, studentLoginIdentity);
  });
}

initializeInstitutionalLogin();

if (loginForms().length) {
  setLoginAvailability(false, "Checking secure login services...");
  checkRequiredLoginServices().then(() => applyRegistrationLoginHandoff());
  window.setInterval(() => checkRequiredLoginServices(), serviceHealthPollIntervalMs);
}

const registerForm = document.querySelector("#registerForm");
const registerMessage = document.querySelector("#registerMessage");
const studentEmailLogin = document.querySelector(".student-email-login");

if (registerForm) {
  registerForm.addEventListener("submit", async (event) => {
    event.preventDefault();

    if (!validateRegistrationReference(registerForm.elements.referenceNumber)) return;

    const studentEmailInput = registerForm.elements.studentEmail;
    if (!umatStudentEmailPattern.test(studentEmailInput.value.trim())) {
      studentEmailInput.setCustomValidity("Enter your official UMaT student email address.");
      studentEmailInput.reportValidity();
      studentEmailInput.addEventListener("input", () => studentEmailInput.setCustomValidity(""), { once: true });
      return;
    }

    const newPassword = document.querySelector("#newPassword").value;
    const repeatPassword = document.querySelector("#repeatPassword").value;

    if (newPassword !== repeatPassword) {
      registerMessage.textContent = "Passwords do not match. Please try again.";
      registerMessage.classList.remove("success");
      return;
    }

    const formData = new FormData(registerForm);
    const departmentSelect = registerForm.elements.department;
    const firstName = String(formData.get("firstName") || "").trim();
    const lastName = String(formData.get("surname") || "").trim();
    const localTelephoneNumber = String(formData.get("telephoneNumber") || "").replace(/\D/g, "");
    const telephoneNumber = `+233${localTelephoneNumber.replace(/^0/, "")}`;
    const student = {
      fullName: `${firstName} ${lastName}`.trim(),
      gender: String(formData.get("gender") || ""),
      referenceNumber: String(formData.get("referenceNumber") || "").trim(),
      studentEmail: String(formData.get("studentEmail") || "").trim().toLowerCase(),
      dateOfBirth: "",
      level: String(formData.get("level") || ""),
      telephoneNumber,
      accountCreationDate: "",
      department: String(formData.get("department") || ""),
      departmentName: departmentSelect.options[departmentSelect.selectedIndex]?.text || ""
    };
    const submitButton = registerForm.querySelector('button[type="submit"]');
    submitButton?.setAttribute("disabled", "");
    try {
      const response = await postApi("/api/auth/register", {
        email: student.studentEmail,
        password: newPassword,
        confirmPassword: repeatPassword,
        firstName,
        middleName: String(formData.get("middleName") || "").trim() || null,
        lastName,
        referenceNumber: student.referenceNumber,
        indexNumber: String(formData.get("indexNumber") || "").trim(),
        telephoneNumber: student.telephoneNumber,
        department: student.departmentName,
        level: student.level,
        gender: student.gender,
        dateOfBirth: String(formData.get("dateOfBirth") || ""),
        acceptTerms: formData.get("acceptTerms") === "on"
      });
      if (!response.ok) {
        registerMessage.textContent = await readApiError(response, "The account could not be created.");
        registerMessage.classList.remove("success");
        return;
      }

      const pending = await response.json();
      sessionStorage.setItem("umatPendingRegistration", JSON.stringify(pending));
      window.location.href = "verify-student-account.html";
    } catch {
      registerMessage.textContent = "The secure registration service is unavailable. Please try again.";
      registerMessage.classList.remove("success");
    } finally {
      submitButton?.removeAttribute("disabled");
    }
  });
}

if (studentEmailLogin) {
  studentEmailLogin.addEventListener("click", () => {
    window.location.href = "homepage.html#login";
  });
}

const revealItems = document.querySelectorAll(".reveal-on-scroll");

if (revealItems.length) {
  if ("IntersectionObserver" in window) {
    const revealObserver = new IntersectionObserver(
      (entries, observer) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("is-visible");
            observer.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.16 }
    );

    revealItems.forEach((item) => revealObserver.observe(item));
  } else {
    revealItems.forEach((item) => item.classList.add("is-visible"));
  }
}

document.querySelectorAll(".faq-question").forEach((button) => {
  button.addEventListener("click", () => {
    const answer = button.nextElementSibling;
    const isOpen = button.getAttribute("aria-expanded") === "true";

    button.setAttribute("aria-expanded", String(!isOpen));
    if (answer) {
      answer.classList.toggle("open", !isOpen);
    }
  });
});

const contactForm = document.querySelector("#contactForm");
const contactMessage = document.querySelector("#contactMessage");

if (contactForm && contactMessage) {
  contactForm.addEventListener("submit", (event) => {
    event.preventDefault();
    contactMessage.textContent = "Message received. The support team will respond shortly.";
    contactForm.reset();
  });
}

const helpSearch = document.querySelector("#helpSearch");
const helpTopics = document.querySelectorAll("[data-help-topic]");

if (helpSearch && helpTopics.length) {
  helpSearch.addEventListener("input", () => {
    const term = helpSearch.value.trim().toLowerCase();
    helpTopics.forEach((topic) => {
      const text = `${topic.textContent} ${topic.dataset.helpTopic}`.toLowerCase();
      topic.style.display = text.includes(term) ? "" : "none";
    });
  });
}

const forgotPasswordForm = document.querySelector("#forgotPasswordForm");
const forgotPasswordMessage = document.querySelector("#forgotPasswordMessage");
const forgotAccountLookup = document.querySelector("#forgotAccountLookup");

if (forgotPasswordForm && forgotPasswordMessage) {
  forgotPasswordForm.addEventListener("submit", (event) => {
    event.preventDefault();
    if (!validateStudentIdentity(forgotAccountLookup)) {
      forgotPasswordMessage.textContent = "Use a valid UMaT student email or enter your complaint reference number.";
      forgotPasswordMessage.classList.add("error");
      return;
    }
    forgotPasswordMessage.classList.remove("error");
    forgotPasswordMessage.innerHTML = 'Reset instructions are ready for the email linked to this account. <a href="reset-password.html">Continue to reset password</a>.';
    forgotPasswordForm.reset();
  });
}

const studentRecoveryLookupForm = document.querySelector("#studentRecoveryLookupForm");
if (studentRecoveryLookupForm) {
  const recoveryState = { recoveryId: "", method: "", verificationToken: "", cooldownTimer: 0 };
  const recoveryMessage = document.querySelector("#studentRecoveryMessage");
  const recoverySteps = [...document.querySelectorAll("[data-recovery-step]")];
  const progressItems = [...document.querySelectorAll("[data-recovery-progress]")];
  const methodForm = document.querySelector("#studentRecoveryMethodForm");
  const methodsContainer = document.querySelector("#studentRecoveryMethods");
  const verifyForm = document.querySelector("#studentRecoveryVerifyForm");
  const codeInputs = [...document.querySelectorAll(".recovery-code-grid input")];
  const passwordForm = document.querySelector("#studentRecoveryPasswordForm");
  const resendButton = document.querySelector("#studentRecoveryResend");

  const showRecoveryStep = (name) => {
    recoverySteps.forEach((step) => { step.hidden = step.dataset.recoveryStep !== name; });
    const order = ["lookup", "method", "verify", "password"];
    const activeIndex = name === "success" ? order.length : order.indexOf(name);
    progressItems.forEach((item, index) => item.classList.toggle("active", index <= activeIndex));
    recoveryMessage.textContent = "";
    recoveryMessage.classList.remove("error");
  };

  const setRecoveryError = (message) => {
    recoveryMessage.textContent = message;
    recoveryMessage.classList.add("error");
  };

  const submitRecoveryRequest = async (path, body) => {
    const response = await postApi(path, body);
    if (!response.ok) throw new Error(await readApiError(response, "The recovery request could not be completed."));
    return response.status === 204 ? {} : response.json().catch(() => ({}));
  };

  const startCooldown = () => {
    window.clearInterval(recoveryState.cooldownTimer);
    let remaining = 60;
    resendButton.disabled = true;
    resendButton.textContent = `Resend code in ${remaining}s`;
    recoveryState.cooldownTimer = window.setInterval(() => {
      remaining -= 1;
      resendButton.textContent = remaining > 0 ? `Resend code in ${remaining}s` : "Resend code";
      if (remaining <= 0) {
        window.clearInterval(recoveryState.cooldownTimer);
        resendButton.disabled = false;
      }
    }, 1000);
  };

  const sendRecoveryCode = async () => {
    await submitRecoveryRequest("/api/auth/student/recovery/send", {
      recoveryId: recoveryState.recoveryId,
      method: recoveryState.method
    });
    document.querySelector("#studentRecoveryDeliveryMessage").textContent =
      `A four-digit code was sent to the selected ${recoveryState.method === "sms" ? "mobile number" : "student email"}. It expires in five minutes.`;
    codeInputs.forEach((input) => { input.value = ""; });
    showRecoveryStep("verify");
    codeInputs[0].focus();
    startCooldown();
  };

  studentRecoveryLookupForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    try {
      const data = await submitRecoveryRequest("/api/auth/student/recovery/lookup", {
        identifier: document.querySelector("#studentRecoveryIdentity").value.trim()
      });
      recoveryState.recoveryId = data.recoveryId;
      methodsContainer.replaceChildren(...data.methods.map((method, index) => {
        const label = document.createElement("label");
        label.className = "recovery-method";
        label.innerHTML = `<input type="radio" name="recoveryMethod" value="${method.method}" ${index === 0 ? "checked" : ""}><strong>${method.method === "sms" ? "Send code to Mobile Number" : "Send code to Student Email"}</strong><small></small>`;
        label.querySelector("small").textContent = method.maskedDestination;
        return label;
      }));
      showRecoveryStep("method");
    } catch (error) { setRecoveryError(error.message); }
  });

  methodForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    recoveryState.method = methodForm.querySelector('input[name="recoveryMethod"]:checked')?.value || "";
    try { await sendRecoveryCode(); } catch (error) { setRecoveryError(error.message); }
  });

  codeInputs.forEach((input, index) => {
    input.addEventListener("input", () => {
      input.value = input.value.replace(/\D/g, "").slice(-1);
      if (input.value) codeInputs[index + 1]?.focus();
    });
    input.addEventListener("keydown", (event) => {
      if (event.key === "Backspace" && !input.value) codeInputs[index - 1]?.focus();
    });
    input.addEventListener("paste", (event) => {
      const digits = event.clipboardData.getData("text").replace(/\D/g, "").slice(0, 4);
      if (digits.length === 4) {
        event.preventDefault();
        codeInputs.forEach((field, digitIndex) => { field.value = digits[digitIndex]; });
        codeInputs[3].focus();
      }
    });
  });

  verifyForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const code = codeInputs.map((input) => input.value).join("");
    if (code.length !== 4) return setRecoveryError("Enter the complete four-digit verification code.");
    try {
      const data = await submitRecoveryRequest("/api/auth/student/recovery/verify", { recoveryId: recoveryState.recoveryId, code });
      recoveryState.verificationToken = data.verificationToken;
      showRecoveryStep("password");
      document.querySelector("#studentRecoveryNewPassword").focus();
    } catch (error) { setRecoveryError(error.message); }
  });

  resendButton.addEventListener("click", async () => {
    try { await sendRecoveryCode(); } catch (error) { setRecoveryError(error.message); }
  });

  passwordForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const newPassword = document.querySelector("#studentRecoveryNewPassword").value;
    const confirmPassword = document.querySelector("#studentRecoveryConfirmPassword").value;
    if (newPassword !== confirmPassword) return setRecoveryError("The new passwords do not match.");
    try {
      await submitRecoveryRequest("/api/auth/student/recovery/complete", {
        recoveryId: recoveryState.recoveryId,
        verificationToken: recoveryState.verificationToken,
        newPassword,
        confirmPassword
      });
      showRecoveryStep("success");
    } catch (error) { setRecoveryError(error.message); }
  });
}

const resetPasswordForm = document.querySelector("#resetPasswordForm");
const resetNewPassword = document.querySelector("#resetNewPassword");
const resetConfirmPassword = document.querySelector("#resetConfirmPassword");
const resetPasswordMessage = document.querySelector("#resetPasswordMessage");

if (resetPasswordForm && resetNewPassword && resetConfirmPassword && resetPasswordMessage) {
  resetPasswordForm.addEventListener("submit", (event) => {
    event.preventDefault();
    resetPasswordMessage.classList.remove("error");

    if (resetNewPassword.value !== resetConfirmPassword.value) {
      resetPasswordMessage.textContent = "Passwords do not match. Please check and try again.";
      resetPasswordMessage.classList.add("error");
      return;
    }

    resetPasswordMessage.textContent = "Password updated successfully. You can now return to login.";
    resetPasswordForm.reset();
  });
}

const superAdminPhoneForm = document.querySelector("#superAdminPhoneForm");

if (superAdminPhoneForm) {
  const panels = [...document.querySelectorAll(".recovery-panel")];
  const progress = [...document.querySelectorAll(".recovery-progress-step")];
  const message = document.querySelector("#superAdminRecoveryMessage");
  const titles = [
    ["Step 1 of 4", "Verify your phone", "We’ll send a one-time code to the phone registered to your account.", "▣"],
    ["Step 2 of 4", "Verify your email", "Enter the verification code sent to your official UMaT email address.", "✉"],
    ["Step 3 of 4", "Confirm your identity", "Answer the security question associated with your Super Admin account.", "?"],
    ["Step 4 of 4", "Create a new password", "Choose a strong password you have not used for this account before.", "●"]
  ];
  let activeStep = 1;
  let timerId;

  function setMessage(text = "", error = false) {
    message.textContent = text;
    message.classList.toggle("error", error);
  }

  function showStep(step) {
    activeStep = step;
    panels.forEach((panel) => { panel.hidden = Number(panel.dataset.step) !== step; });
    progress.forEach((item, index) => {
      item.classList.toggle("is-active", index + 1 === step);
      item.classList.toggle("is-complete", index + 1 < step);
      item.disabled = index + 1 > step;
      item.toggleAttribute("aria-current", index + 1 === step);
    });
    document.querySelector("#recoveryStepLabel").textContent = titles[step - 1][0];
    document.querySelector("#recoveryStepTitle").textContent = titles[step - 1][1];
    document.querySelector("#recoveryStepDescription").textContent = titles[step - 1][2];
    document.querySelector("#recoveryStepIcon").textContent = titles[step - 1][3];
    setMessage();
  }

  function startTimer(output) {
    clearInterval(timerId);
    let remaining = 300;
    const paint = () => {
      const minutes = String(Math.floor(remaining / 60)).padStart(2, "0");
      const seconds = String(remaining % 60).padStart(2, "0");
      output.textContent = remaining ? `Expires in ${minutes}:${seconds}` : "Code expired";
      remaining = Math.max(0, remaining - 1);
    };
    paint();
    timerId = setInterval(paint, 1000);
  }

  function validCode(input) {
    if (!/^\d{6}$/.test(input.value.trim())) {
      setMessage("Enter the complete 6-digit verification code.", true);
      input.focus();
      return false;
    }
    return true;
  }

  superAdminPhoneForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const phone = document.querySelector("#superAdminPhone");
    if (phone.value.replace(/\D/g, "").length < 9) return setMessage("Enter the phone number registered to your account.", true);
    document.querySelector("#phoneCodeArea").hidden = false;
    startTimer(document.querySelector("#phoneTimer"));
    setMessage("A one-time code has been sent to your registered phone.");
  });

  document.querySelector("#verifyPhoneCode").addEventListener("click", () => {
    if (validCode(document.querySelector("#phoneCode"))) showStep(2);
  });

  document.querySelector("#superAdminEmailForm").addEventListener("submit", async (event) => {
    event.preventDefault();
    const email = document.querySelector("#superAdminRecoveryEmail");
    if (!email.validity.valid) return setMessage("Enter a valid official UMaT email address.", true);
    const button = event.submitter;
    button.disabled = true;
    button.firstChild.textContent = "Sending securely ";
    try {
      const response = await postApi("/api/auth/admin/forgot-password", { email: email.value.trim().toLowerCase() });
      if (!response.ok) throw new Error(await readApiError(response, "Unable to send the verification code."));
      document.querySelector("#emailCodeArea").hidden = false;
      startTimer(document.querySelector("#emailTimer"));
      setMessage("A verification code has been sent to your official email.");
    } catch (error) { setMessage(error.message || "Unable to reach the recovery service.", true); }
    finally { button.disabled = false; button.firstChild.textContent = "Send verification code "; }
  });

  document.querySelector("#verifyEmailCode").addEventListener("click", () => {
    if (validCode(document.querySelector("#emailCode"))) showStep(3);
  });

  document.querySelector("#superAdminQuestionForm").addEventListener("submit", (event) => {
    event.preventDefault();
    const answer = document.querySelector("#securityAnswer");
    if (answer.value.trim().length < 2) return setMessage("Enter your security answer.", true);
    showStep(4);
  });

  const newPassword = document.querySelector("#superAdminNewPassword");
  const confirmPassword = document.querySelector("#superAdminConfirmPassword");
  const checks = {
    length: (value) => value.length >= 12,
    upper: (value) => /[A-Z]/.test(value), lower: (value) => /[a-z]/.test(value),
    number: (value) => /\d/.test(value), special: (value) => /[^A-Za-z0-9]/.test(value)
  };
  newPassword.addEventListener("input", () => {
    const score = Object.entries(checks).reduce((sum, [rule, test]) => {
      const valid = test(newPassword.value);
      document.querySelector(`[data-rule="${rule}"]`).classList.toggle("is-valid", valid);
      return sum + Number(valid);
    }, 0);
    const bar = document.querySelector("#strengthBar");
    bar.style.width = `${score * 20}%`;
    bar.style.background = score < 3 ? "#d35252" : score < 5 ? "#d49a33" : "#26864c";
    document.querySelector("#strengthText").textContent = ["Enter a strong password", "Very weak", "Weak", "Fair", "Good", "Strong"][score];
  });
  document.querySelectorAll(".recovery-reveal").forEach((button) => button.addEventListener("click", () => {
    const input = button.parentElement.querySelector("input");
    input.type = input.type === "password" ? "text" : "password";
    button.textContent = input.type === "password" ? "Show" : "Hide";
  }));
  document.querySelector("#superAdminPasswordForm").addEventListener("submit", (event) => {
    event.preventDefault();
    if (!Object.values(checks).every((test) => test(newPassword.value))) return setMessage("Your password does not meet all security requirements.", true);
    if (newPassword.value !== confirmPassword.value) return setMessage("The new passwords do not match.", true);
    clearInterval(timerId);
    panels.forEach((panel) => { panel.hidden = true; });
    document.querySelector(".recovery-card-top").hidden = true;
    document.querySelector("#recoverySuccess").hidden = false;
    message.hidden = true;
    progress.forEach((item) => { item.classList.remove("is-active"); item.classList.add("is-complete"); });
  });
}

const adminForgotPasswordForm = document.querySelector("#adminForgotPasswordForm");
const adminRecoveryEmail = document.querySelector("#adminRecoveryEmail");
const adminForgotPasswordMessage = document.querySelector("#adminForgotPasswordMessage");

if (adminForgotPasswordForm && adminRecoveryEmail && adminForgotPasswordMessage) {
  adminForgotPasswordForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    adminForgotPasswordMessage.classList.remove("error");
    const submitButton = adminForgotPasswordForm.querySelector('button[type="submit"]');
    submitButton.disabled = true;
    submitButton.textContent = "Sending securely...";

    try {
      const response = await postApi("/api/auth/admin/forgot-password", {
        email: adminRecoveryEmail.value.trim().toLowerCase()
      });
      if (!response.ok) {
        throw new Error(await readApiError(response, "Unable to request a password reset."));
      }
      adminForgotPasswordMessage.textContent =
        "If this email belongs to an eligible staff or administrator account, secure reset instructions have been sent.";
      adminForgotPasswordForm.reset();
    } catch (error) {
      adminForgotPasswordMessage.textContent = error.message || "Unable to connect to the recovery service.";
      adminForgotPasswordMessage.classList.add("error");
    } finally {
      submitButton.disabled = false;
      submitButton.textContent = "Send secure reset link";
    }
  });
}

const adminResetPasswordForm = document.querySelector("#adminResetPasswordForm");
const adminResetNewPassword = document.querySelector("#adminResetNewPassword");
const adminResetConfirmPassword = document.querySelector("#adminResetConfirmPassword");
const adminResetPasswordMessage = document.querySelector("#adminResetPasswordMessage");

if (adminResetPasswordForm && adminResetNewPassword && adminResetConfirmPassword && adminResetPasswordMessage) {
  const resetParameters = new URLSearchParams(window.location.search);
  const resetEmail = resetParameters.get("email") || "";
  const resetToken = resetParameters.get("token") || "";
  const submitButton = adminResetPasswordForm.querySelector('button[type="submit"]');

  if (!resetEmail || !resetToken) {
    adminResetPasswordMessage.textContent = "This recovery link is incomplete. Request a new secure reset link.";
    adminResetPasswordMessage.classList.add("error");
    submitButton.disabled = true;
  }

  adminResetPasswordForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    adminResetPasswordMessage.classList.remove("error");

    if (adminResetNewPassword.value !== adminResetConfirmPassword.value) {
      adminResetPasswordMessage.textContent = "The new passwords do not match.";
      adminResetPasswordMessage.classList.add("error");
      return;
    }

    submitButton.disabled = true;
    submitButton.textContent = "Updating securely...";
    try {
      const response = await postApi("/api/auth/reset-password", {
        email: resetEmail,
        token: resetToken,
        newPassword: adminResetNewPassword.value,
        confirmPassword: adminResetConfirmPassword.value
      });
      if (!response.ok) {
        throw new Error(await readApiError(response, "The recovery link is invalid or expired."));
      }
      adminResetPasswordMessage.innerHTML =
        'Password updated successfully. <a href="admin-login.html">Return to staff/admin login</a>.';
      adminResetPasswordForm.reset();
      submitButton.disabled = true;
    } catch (error) {
      adminResetPasswordMessage.textContent = error.message || "Unable to update the password.";
      adminResetPasswordMessage.classList.add("error");
      submitButton.disabled = false;
      submitButton.textContent = "Update administrator password";
    }
  });
}

const verifyEmailForm = document.querySelector("#verifyEmailForm");
const verifyEmailMessage = document.querySelector("#verifyEmailMessage");
const verifyCodeInputs = document.querySelectorAll(".flow-code-grid input");
const resendVerification = document.querySelector("#resendVerification");

if (verifyCodeInputs.length) {
  verifyCodeInputs.forEach((input, index) => {
    input.addEventListener("input", () => {
      input.value = input.value.replace(/\D/g, "").slice(0, 1);
      if (input.value && verifyCodeInputs[index + 1]) {
        verifyCodeInputs[index + 1].focus();
      }
    });

    input.addEventListener("keydown", (event) => {
      if (event.key === "Backspace" && !input.value && verifyCodeInputs[index - 1]) {
        verifyCodeInputs[index - 1].focus();
      }
    });
  });
}

if (verifyEmailForm && verifyEmailMessage) {
  verifyEmailForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const code = Array.from(verifyCodeInputs).map((input) => input.value).join("");

    if (code.length < 6) {
      verifyEmailMessage.textContent = "Enter the complete six-digit verification code.";
      verifyEmailMessage.classList.add("error");
      return;
    }

    verifyEmailMessage.classList.remove("error");
    verifyEmailMessage.textContent = "Email verified successfully. You can now continue to login.";
    verifyEmailForm.reset();
  });
}

if (resendVerification && verifyEmailMessage) {
  resendVerification.addEventListener("click", (event) => {
    event.preventDefault();
    verifyEmailMessage.classList.remove("error");
    verifyEmailMessage.textContent = "A new verification code has been prepared for your registered email.";
  });
}

const adminWorkEmailPattern = /^[A-Z0-9._%+-]+@umat\.edu\.gh$/i;
const adminLoginForm = document.querySelector("#adminLoginForm");
const adminLoginEmail = document.querySelector("#adminLoginEmail");
const adminLoginPassword = document.querySelector("#adminLoginPassword");
const adminLoginRole = document.querySelector("#adminLoginRole");
const adminLoginDepartmentField = document.querySelector("#adminLoginDepartmentField");
const adminLoginDepartment = document.querySelector("#adminLoginDepartment");
const adminLoginIdentityLabel = document.querySelector("#adminLoginIdentityLabel");
const adminLoginSubmit = document.querySelector("#adminLoginSubmit");
const adminRoleInfo = document.querySelector("#adminRoleInfo");
const adminLoginMessage = document.querySelector("#adminLoginMessage");
const adminRegisterForm = document.querySelector("#adminRegisterForm");
const adminRegisterEmail = document.querySelector("#adminRegisterEmail");
const adminRegisterPassword = document.querySelector("#adminRegisterPassword");
const adminRegisterConfirm = document.querySelector("#adminRegisterConfirm");
const adminRegisterMessage = document.querySelector("#adminRegisterMessage");
const superAdminSetupForm = document.querySelector("#superAdminSetupForm");
const superAdminSetupSubmit = document.querySelector("#superAdminSetupSubmit");
const superAdminSetupMessage = document.querySelector("#superAdminSetupMessage");
const staffTwoFactorForm = document.querySelector("#staffTwoFactorForm");
const staffTwoFactorCode = document.querySelector("#staffTwoFactorCode");
const staffTwoFactorMessage = document.querySelector("#staffTwoFactorMessage");
const staffTwoFactorKeyPanel = document.querySelector("#staffTwoFactorKeyPanel");
const staffTwoFactorKey = document.querySelector("#staffTwoFactorKey");
const staffTwoFactorInstructions = document.querySelector("#staffTwoFactorInstructions");
let pendingStaffChallenge = null;
let pendingStaffSetup = false;

function setAdminMessage(element, text, isError = false) {
  if (!element) return;
  element.textContent = text;
  element.classList.toggle("error", isError);
}

function isAdminWorkEmail(value) {
  return adminWorkEmailPattern.test(value.trim());
}

const staffRoleDestinations = {
  department_officer: "dashboards/department-officer/department-dashboard.html",
  admin: "dashboards/admin/admin-dashboard.html",
  super_admin: "dashboards/super-admin/super-admin-dashboard.html"
};

const staffRoleDescriptions = {
  department_officer: "Department Officers manage complaints assigned to their department.",
  admin: "Administrators coordinate complaints, users, departments and reports.",
  super_admin: "Super Administrators control security, configuration, administrators and system operations."
};

async function refreshInitialAdminSetup() {
  if (!superAdminSetupForm || !adminLoginForm) return;
  try {
    const response = await fetchApi("/api/setup/status", { cache: "no-store" }, 3);
    if (!response.ok) throw new Error(await readApiError(response, "Unable to check administrator setup status."));
    const status = await response.json();
    superAdminSetupForm.hidden = !status.requiresSetup;
    adminLoginForm.hidden = status.requiresSetup;
    if (status.requiresSetup) setAdminMessage(superAdminSetupMessage, "Create the first authorised Super Administrator to activate staff access.");
  } catch (error) {
    superAdminSetupForm.hidden = true;
    adminLoginForm.hidden = false;
    setAdminMessage(adminLoginMessage, error.message || "Unable to check administrator setup status.", true);
  }
}

if (superAdminSetupForm && superAdminSetupMessage) {
  superAdminSetupForm.addEventListener("submit", async event => {
    event.preventDefault();
    if (!superAdminSetupForm.reportValidity()) return;
    const data = Object.fromEntries(new FormData(superAdminSetupForm));
    if (!isAdminWorkEmail(data.email || "")) {
      setAdminMessage(superAdminSetupMessage, "Use an official staff email ending in @umat.edu.gh.", true);
      document.querySelector("#setupEmail")?.focus();
      return;
    }
    if (data.password !== data.confirmPassword) {
      setAdminMessage(superAdminSetupMessage, "Passwords do not match.", true);
      document.querySelector("#setupConfirmPassword")?.focus();
      return;
    }
    const givenNames = [data.firstName, data.otherNames].map(value => String(value || "").trim()).filter(Boolean).join(" ");
    if (givenNames.length > 80) {
      setAdminMessage(superAdminSetupMessage, "First name and other names must contain no more than 80 characters in total.", true);
      document.querySelector("#setupOtherNames")?.focus();
      return;
    }
    superAdminSetupSubmit.disabled = true;
    superAdminSetupSubmit.setAttribute("aria-busy", "true");
    superAdminSetupSubmit.textContent = "Creating account...";
    setAdminMessage(superAdminSetupMessage, "Saving the Super Administrator to the live database...");
    try {
      const response = await postApi("/api/setup/super-admin", {
        firstName: givenNames,
        lastName: data.lastName,
        administratorId: data.administratorId || null,
        email: data.email,
        phoneNumber: data.phoneNumber,
        password: data.password,
        confirmPassword: data.confirmPassword
      });
      if (!response.ok) throw new Error(await readApiError(response, "Unable to create the Super Administrator."));
      const created = await response.json();
      superAdminSetupForm.reset();
      superAdminSetupForm.hidden = true;
      adminLoginForm.hidden = false;
      adminLoginRole.value = "super_admin";
      adminRoleInfo.textContent = staffRoleDescriptions.super_admin;
      adminLoginEmail.value = data.email;
      setAdminMessage(adminLoginMessage, `Super Administrator ${created.administratorId} was created successfully. Sign in with the password you just created.`);
    } catch (error) {
      setAdminMessage(superAdminSetupMessage, error.message || "Unable to create the Super Administrator.", true);
    } finally {
      superAdminSetupSubmit.disabled = false;
      superAdminSetupSubmit.removeAttribute("aria-busy");
      superAdminSetupSubmit.textContent = "Create Super Administrator";
    }
  });
}

refreshInitialAdminSetup();

const administrativeUnitDashboardSlugs = {
  "Registry":"registry",
  "ICT Directorate":"ict-directorate",
  "Finance Office":"finance-office",
  "University Library":"university-library",
  "Student Affairs":"student-affairs",
  "Hall/Hostel Administration":"hall-hostel-administration",
  "Security Unit":"security-unit",
  "Works and Physical Development":"works-and-physical-development",
  "Transport Unit":"transport-unit",
  "Human Resource Unit":"human-resource-unit",
  "Health Services":"health-services",
  "Procurement Unit":"procurement-unit",
  "Academic Affairs / Examinations":"academic-affairs-examinations",
  "Counselling Unit":"counselling-unit",
  "Quality Assurance Unit":"quality-assurance-unit"
};

async function openStaffDashboard(result) {
  if (!result.authenticated || !result.user) throw new Error("The staff login was not completed.");
  const roles = result.user.roles || [];
  const actualRole = roles.includes("SuperAdmin") ? "super_admin" : roles.includes("Admin") ? "admin" : roles.includes("DepartmentOfficer") ? "department_officer" : null;
  if (!actualRole) throw new Error("This account does not have an authorised staff role.");
  const activeRole = ({SuperAdmin:"super_admin",Admin:"admin",DepartmentOfficer:"department_officer"})[result.user.activeRole] || actualRole;
  if (result.user.forcePasswordReset) {
    window.location.href = `dashboards/${activeRole === "super_admin" ? "super-admin" : activeRole === "department_officer" ? "department-officer" : "admin"}/change-password.html?firstLogin=true`;
    return;
  }
  if (activeRole === "department_officer" && result.user.activeDepartmentId) {
    const departmentsResponse = await fetchApi("/api/departments", { cache:"no-store" });
    if (!departmentsResponse.ok) throw new Error("Unable to load the assigned Administrative Unit.");
    const departments = await departmentsResponse.json();
    const unit = departments.find(item => item.id === result.user.activeDepartmentId);
    const slug = administrativeUnitDashboardSlugs[unit?.name];
    if (!slug) throw new Error("No dashboard page is configured for the assigned Administrative Unit.");
    window.location.href = `dashboards/department-officer/${slug}-dashboard.html`;
    return;
  }
  window.location.href = staffRoleDestinations[activeRole];
}

async function beginStaffTwoFactor(result) {
  pendingStaffChallenge = result.challengeToken;
  pendingStaffSetup = Boolean(result.requiresTwoFactorSetup);
  adminLoginForm.hidden = true;
  staffTwoFactorForm.hidden = false;
  staffTwoFactorKeyPanel.hidden = !pendingStaffSetup;
  if (pendingStaffSetup) {
    const response = await postApi("/api/auth/two-factor/setup", { challengeToken: pendingStaffChallenge });
    if (!response.ok) throw new Error(await readApiError(response, "Unable to start two-factor setup."));
    const setup = await response.json();
    staffTwoFactorKey.textContent = setup.sharedKey;
    staffTwoFactorInstructions.textContent = "Add this setup key to your authenticator application, then enter its current code.";
  } else {
    staffTwoFactorInstructions.textContent = "Enter the current code from your authenticator application.";
  }
  staffTwoFactorCode.focus();
}

staffTwoFactorForm?.addEventListener("submit", async event => {
  event.preventDefault();
  try {
    const path = pendingStaffSetup ? "/api/auth/two-factor/enable" : "/api/auth/two-factor/verify";
    const response = await postApi(path, { challengeToken: pendingStaffChallenge, code: staffTwoFactorCode.value.trim(), rememberMachine: false });
    if (!response.ok) throw new Error(await readApiError(response, "The authentication code was not accepted."));
    await openStaffDashboard(await response.json());
  } catch (error) {
    setAdminMessage(staffTwoFactorMessage, error.message || "Unable to complete two-factor authentication.", true);
  }
});

document.querySelector("#staffTwoFactorCancel")?.addEventListener("click", () => {
  pendingStaffChallenge = null;
  pendingStaffSetup = false;
  staffTwoFactorForm.reset();
  staffTwoFactorForm.hidden = true;
  adminLoginForm.hidden = false;
});

if (adminLoginRole && adminRoleInfo) {
  const staffForgotPasswordLink = document.querySelector("#staffForgotPasswordLink");
  const updateStaffRecoveryLink = () => {
    if (!staffForgotPasswordLink) return;
    staffForgotPasswordLink.href = adminLoginRole.value === "department_officer"
      ? "department-officer-forgot-password.html"
      : "admin-forgot-password.html";
  };
  const updateStaffLoginFields = () => {
    const isOfficer = adminLoginRole.value === "department_officer";
    if (adminLoginDepartmentField) adminLoginDepartmentField.hidden = !isOfficer;
    if (adminLoginDepartment) {
      adminLoginDepartment.required = isOfficer;
      if (!isOfficer) adminLoginDepartment.value = "";
    }
    if (adminLoginIdentityLabel) adminLoginIdentityLabel.textContent = isOfficer
      ? "Official email address or username"
      : "Official UMaT email";
    if (adminLoginEmail) adminLoginEmail.placeholder = isOfficer
      ? "officer@umat.edu.gh or Staff ID"
      : "staff.name@umat.edu.gh";
  };
  const loadAdministrativeUnits = async () => {
    if (!adminLoginDepartment || adminLoginDepartment.options.length > 1) return;
    try {
      const response = await fetchApi("/api/departments", { cache: "no-store" });
      if (!response.ok) throw new Error("Administrative units could not be loaded.");
      const departments = await response.json();
      departments.filter((unit) => unit.isActive !== false).forEach((unit) => {
        const option = document.createElement("option");
        option.value = unit.id;
        option.textContent = unit.name;
        adminLoginDepartment.append(option);
      });
    } catch (error) {
      setAdminMessage(adminLoginMessage, error.message, true);
    }
  };
  const loginParameters = new URLSearchParams(window.location.search);
  const requestedRole = loginParameters.get("role");
  if (staffRoleDestinations[requestedRole]) {
    adminLoginRole.value = requestedRole;
    adminRoleInfo.textContent = staffRoleDescriptions[requestedRole];
  }
  updateStaffRecoveryLink();
  updateStaffLoginFields();
  loadAdministrativeUnits();
  if (loginParameters.get("reason") === "unauthorized" && adminLoginMessage) {
    setAdminMessage(adminLoginMessage, "Please sign in with an authorised staff account to continue.", true);
  }
  adminLoginRole.addEventListener("change", () => {
    adminRoleInfo.textContent = staffRoleDescriptions[adminLoginRole.value];
    updateStaffRecoveryLink();
    updateStaffLoginFields();
  });
}

const officerQuestionForm = document.querySelector("#officerQuestionForm");
if (officerQuestionForm) {
  const startPanel = document.querySelector("#officerRecoveryStart");
  const otpRequestForm = document.querySelector("#officerOtpRequestForm");
  const otpVerifyForm = document.querySelector("#officerOtpVerifyForm");
  const passwordForm = document.querySelector("#officerNewPasswordForm");
  const successPanel = document.querySelector("#officerRecoverySuccess");
  const message = document.querySelector("#officerRecoveryMessage");
  const progress = [...document.querySelectorAll(".officer-recovery-progress li")];
  const codeInputs = [...document.querySelectorAll("#officerOtpVerifyForm .recovery-code-grid input")];
  const resendButton = document.querySelector("#officerOtpResend");
  let resendTimer = 0;

  const setOfficerRecoveryMessage = (text = "", isError = false) => {
    message.textContent = text;
    message.classList.toggle("error", isError);
  };
  const identityIsValid = () => {
    const value = document.querySelector("#officerRecoveryIdentity").value.trim();
    if (!value) {
      setOfficerRecoveryMessage("Enter your official UMaT email or Staff ID.", true);
      return false;
    }
    return true;
  };
  const showOfficerStep = (step) => {
    startPanel.hidden = step !== 0;
    otpRequestForm.hidden = step !== 1;
    otpVerifyForm.hidden = step !== 2;
    passwordForm.hidden = step !== 3;
    successPanel.hidden = step !== 4;
    progress.forEach((item, index) => item.classList.toggle("active", index <= Math.min(step, 1)));
    setOfficerRecoveryMessage();
  };
  const startResendTimer = () => {
    window.clearInterval(resendTimer);
    let seconds = 60;
    resendButton.disabled = true;
    resendButton.textContent = `Resend code in ${seconds}s`;
    resendTimer = window.setInterval(() => {
      seconds -= 1;
      resendButton.textContent = seconds ? `Resend code in ${seconds}s` : "Resend code";
      if (!seconds) { window.clearInterval(resendTimer); resendButton.disabled = false; }
    }, 1000);
  };

  officerQuestionForm.addEventListener("submit", (event) => {
    event.preventDefault();
    if (!identityIsValid() || !officerQuestionForm.reportValidity()) return;
    showOfficerStep(1);
    document.querySelector('input[name="officerOtpChannel"]:checked')?.focus();
  });
  otpRequestForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const channel = document.querySelector('input[name="officerOtpChannel"]:checked')?.value || "phone";
    const destination = channel === "email" ? "official UMaT email" : "registered phone";
    document.querySelector("#officerOtpHelp").textContent = `Enter the four-digit OTP sent to your ${destination}. It expires in five minutes.`;
    showOfficerStep(2);
    setOfficerRecoveryMessage(`A four-digit OTP has been sent to your ${destination}.`);
    codeInputs[0].focus();
    startResendTimer();
  });
  codeInputs.forEach((input, index) => {
    input.addEventListener("input", () => {
      input.value = input.value.replace(/\D/g, "").slice(-1);
      if (input.value) codeInputs[index + 1]?.focus();
    });
    input.addEventListener("keydown", (event) => {
      if (event.key === "Backspace" && !input.value) codeInputs[index - 1]?.focus();
    });
    input.addEventListener("paste", (event) => {
      const digits = event.clipboardData.getData("text").replace(/\D/g, "").slice(0, 4);
      if (digits.length !== 4) return;
      event.preventDefault();
      codeInputs.forEach((field, digitIndex) => { field.value = digits[digitIndex]; });
      codeInputs[3].focus();
    });
  });
  otpVerifyForm.addEventListener("submit", (event) => {
    event.preventDefault();
    if (codeInputs.map((input) => input.value).join("").length !== 4) {
      setOfficerRecoveryMessage("Enter the complete four-digit OTP.", true);
      return;
    }
    showOfficerStep(3);
    document.querySelector("#officerNewPassword").focus();
  });
  resendButton.addEventListener("click", () => {
    codeInputs.forEach((input) => { input.value = ""; });
    const channel = document.querySelector('input[name="officerOtpChannel"]:checked')?.value || "phone";
    setOfficerRecoveryMessage(`A new four-digit OTP has been sent to your ${channel === "email" ? "official UMaT email" : "registered phone"}.`);
    codeInputs[0].focus();
    startResendTimer();
  });
  passwordForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const password = document.querySelector("#officerNewPassword").value;
    const confirmation = document.querySelector("#officerConfirmPassword").value;
    if (password !== confirmation) {
      setOfficerRecoveryMessage("The new passwords do not match.", true);
      return;
    }
    if (!passwordForm.reportValidity()) return;
    showOfficerStep(4);
  });
}

if (adminLoginForm && adminLoginEmail && adminLoginPassword && adminLoginRole && adminLoginMessage) {
  adminLoginForm.addEventListener("submit", async (event) => {
    event.preventDefault();

    adminLoginMessage.classList.remove("error");

    const isOfficerLogin = adminLoginRole.value === "department_officer";
    const loginIdentifier = adminLoginEmail.value.trim();
    if (!loginIdentifier || (!isOfficerLogin && !isAdminWorkEmail(loginIdentifier))) {
      setAdminMessage(adminLoginMessage, isOfficerLogin
        ? "Enter your official UMaT email address or username."
        : "Use your official staff email ending in @umat.edu.gh.", true);
      adminLoginEmail.focus();
      return;
    }

    if (isOfficerLogin && !adminLoginDepartment?.value) {
      setAdminMessage(adminLoginMessage, "Select your assigned administrative unit.", true);
      adminLoginDepartment?.focus();
      return;
    }

    if (adminLoginPassword.value.length < 8) {
      setAdminMessage(adminLoginMessage, "Enter a password containing at least eight characters.", true);
      adminLoginPassword.focus();
      return;
    }

    setAdminMessage(adminLoginMessage, "Checking your staff account...");
    if (adminLoginSubmit) {
      adminLoginSubmit.setAttribute("aria-busy", "true");
      adminLoginSubmit.disabled = true;
      adminLoginSubmit.textContent = "Signing in...";
    }
    try {
      const response = await postApi("/api/auth/staff/login", {
        email: loginIdentifier,
        identifier: loginIdentifier,
        password: adminLoginPassword.value,
        rememberMe: false,
        loginPage: adminLoginRole.value,
        activeDepartmentId: isOfficerLogin ? adminLoginDepartment.value : null
      });
      if (!response.ok) {
        const setupResponse = await fetchApi("/api/setup/status", { cache: "no-store" });
        const setup = setupResponse.ok ? await setupResponse.json() : null;
        const fallback = setup?.requiresSetup
          ? "No administrator account has been configured. Complete the initial Super Administrator setup first."
          : isOfficerLogin ? "Invalid Department Officer email or password." : "Invalid administrator email or password.";
        throw new Error(await readApiError(response, fallback));
      }
      const result = await response.json();
      if (result.requiresTwoFactor || result.requiresTwoFactorSetup) {
        await beginStaffTwoFactor(result);
        if (adminLoginSubmit) {
          adminLoginSubmit.removeAttribute("aria-busy");
          adminLoginSubmit.disabled = false;
          adminLoginSubmit.textContent = "Sign in securely";
        }
        return;
      }
      setAdminMessage(adminLoginMessage, "Sign-in successful. Opening your dashboard...");
      await openStaffDashboard(result);
    } catch (error) {
      setAdminMessage(adminLoginMessage, error.message || "Unable to complete staff sign-in.", true);
      if (adminLoginSubmit) {
        adminLoginSubmit.removeAttribute("aria-busy");
        adminLoginSubmit.disabled = false;
        adminLoginSubmit.textContent = "Sign in securely";
      }
    }
  });
}

if (adminRegisterForm && adminRegisterEmail && adminRegisterPassword && adminRegisterConfirm && adminRegisterMessage) {
  adminRegisterForm.addEventListener("submit", (event) => {
    event.preventDefault();

    if (!isAdminWorkEmail(adminRegisterEmail.value)) {
      setAdminMessage(adminRegisterMessage, "Admin registration requires an official staff email ending in @umat.edu.gh.", true);
      adminRegisterEmail.focus();
      return;
    }

    if (adminRegisterPassword.value !== adminRegisterConfirm.value) {
      setAdminMessage(adminRegisterMessage, "Passwords do not match. Please check and try again.", true);
      adminRegisterConfirm.focus();
      return;
    }

    setAdminMessage(adminRegisterMessage, "Admin access request submitted for review.");
    adminRegisterForm.reset();
  });
}
