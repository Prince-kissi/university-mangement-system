(() => {
  const methodForm = document.querySelector("#studentVerificationMethodForm");
  if (!methodForm) return;
  const pending = JSON.parse(sessionStorage.getItem("umatPendingRegistration") || "null");
  if (!pending?.pendingRegistrationId) {
    window.location.replace("register.html");
    return;
  }
  const codeForm = document.querySelector("#studentVerificationCodeForm");
  const inputs = [...codeForm.querySelectorAll(".student-code-grid input")];
  const message = document.querySelector("#studentVerificationMessage");
  const sendButton = document.querySelector("#sendStudentVerificationCode");
  const resendButton = document.querySelector("#resendStudentVerificationCode");
  let method = "";
  let expiryTimer;
  let resendTimer;

  document.querySelector("#studentMaskedPhone").textContent = pending.maskedPhone || "Registered mobile";
  document.querySelector("#studentMaskedEmail").textContent = pending.maskedEmail || "Registered UMaT email";
  const setMessage = (text = "", error = false) => { message.textContent = text; message.classList.toggle("error", error); };
  const loading = (button, active, label) => { button.disabled = active; button.classList.toggle("is-loading", active); if (label) button.textContent = label; };

  methodForm.querySelectorAll('input[name="verificationMethod"]').forEach((radio) => radio.addEventListener("change", () => {
    method = radio.value;
    sendButton.disabled = false;
    setMessage();
  }));
  const startTimers = (expiresAt, resendAvailableAt) => {
    clearInterval(expiryTimer); clearInterval(resendTimer);
    const timer = document.querySelector("#studentCodeTimer");
    const paintExpiry = () => {
      const left = Math.max(0, Math.ceil((new Date(expiresAt) - Date.now()) / 1000));
      timer.textContent = `${String(Math.floor(left / 60)).padStart(2,"0")}:${String(left % 60).padStart(2,"0")}`;
      if (!left) { clearInterval(expiryTimer); setMessage("OTP expired. Request a new code.", true); }
    };
    const paintResend = () => {
      const left = Math.max(0, Math.ceil((new Date(resendAvailableAt) - Date.now()) / 1000));
      resendButton.disabled = left > 0;
      resendButton.textContent = left ? `Resend code in ${left}s` : "Resend code";
      if (!left) clearInterval(resendTimer);
    };
    paintExpiry(); paintResend();
    expiryTimer = setInterval(paintExpiry, 1000); resendTimer = setInterval(paintResend, 1000);
  };
  const sendCode = async (endpoint = "/api/auth/students/send-verification-code") => {
    const response = await postApi(endpoint, { pendingRegistrationId: pending.pendingRegistrationId, method });
    if (!response.ok) throw new Error(await readApiError(response, "Unable to send the verification code."));
    const result = await response.json();
    document.querySelector("#studentCodeDestination").textContent = result.maskedDestination;
    document.querySelector("#studentVerificationMethodTitle").textContent = method === "sms" ? "SMS Verification" : "Email Verification";
    document.querySelector("#verifyStudentAccount").textContent = method === "sms" ? "Verify OTP" : "Verify Code";
    methodForm.hidden = true; codeForm.hidden = false;
    inputs.forEach((input) => { input.value = ""; }); inputs[0].focus();
    startTimers(result.expiresAt, result.resendAvailableAt);
    setMessage("OTP sent successfully.");
  };
  methodForm.addEventListener("submit", async (event) => {
    event.preventDefault(); if (!method) return;
    loading(sendButton, true, "Sending code…");
    try { await sendCode(); } catch (error) { setMessage(error.message, true); }
    finally { loading(sendButton, false, "Send Verification Code"); }
  });
  inputs.forEach((input, index) => {
    input.addEventListener("input", () => { input.value = input.value.replace(/\D/g, "").slice(-1); if (input.value) inputs[index + 1]?.focus(); });
    input.addEventListener("keydown", (event) => { if (event.key === "Backspace" && !input.value) inputs[index - 1]?.focus(); });
    input.addEventListener("paste", (event) => { const digits = event.clipboardData.getData("text").replace(/\D/g, "").slice(0,6); if (digits.length !== 6) return; event.preventDefault(); inputs.forEach((field,i) => { field.value=digits[i]; }); inputs[5].focus(); });
  });
  codeForm.addEventListener("submit", async (event) => {
    event.preventDefault(); const code = inputs.map((input) => input.value).join("");
    if (code.length !== 6) return setMessage("Enter the complete six-digit code.", true);
    const button = document.querySelector("#verifyStudentAccount"); loading(button, true, "Verifying…");
    try {
      const response = await postApi("/api/auth/students/verify-code", { pendingRegistrationId: pending.pendingRegistrationId, code });
      if (!response.ok) throw new Error(await readApiError(response, "Invalid OTP. Please try again."));
      clearInterval(expiryTimer); clearInterval(resendTimer); codeForm.hidden = true;
      const success = document.querySelector("#studentVerificationSuccess");
      success.hidden = false; success.classList.add("is-visible"); document.querySelector(".student-verification-back").hidden = true;
      sessionStorage.removeItem("umatPendingRegistration"); setMessage();
      let seconds = 3;
      const redirectTimer = document.querySelector("#studentRedirectTimer");
      const redirectInterval = setInterval(() => {
        seconds -= 1;
        redirectTimer.textContent = String(Math.max(0, seconds));
        if (seconds <= 0) {
          clearInterval(redirectInterval);
          window.location.replace("login.html?verified=1");
        }
      }, 1000);
    } catch (error) { setMessage(error.message, true); inputs.forEach((input) => { input.value=""; }); inputs[0].focus(); }
    finally { loading(button, false, "Verify Account"); }
  });
  resendButton.addEventListener("click", async () => {
    loading(resendButton, true, "Sending…");
    try { await sendCode("/api/auth/students/resend-verification-code"); }
    catch (error) { setMessage(error.message, true); }
    finally { resendButton.classList.remove("is-loading"); }
  });
  document.querySelector("#changeStudentVerificationMethod").addEventListener("click", () => { codeForm.hidden = true; methodForm.hidden = false; setMessage(); });
})();
