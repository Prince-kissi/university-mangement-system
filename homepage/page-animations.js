/* ════════════════════════════════════════════════════
   UMAT — Shared Page Interactions
   Scroll-reveal, animated counters, FAQ accordions, nav toggle
   ════════════════════════════════════════════════════ */

(function () {
  "use strict";

  /* ── Mobile nav toggle (re-bind in case app.js already ran) ── */
  const menuToggle = document.querySelector(".menu-toggle");
  const navMenu = document.querySelector("#navMenu");
  if (menuToggle && navMenu && !menuToggle.dataset.bound) {
    menuToggle.dataset.bound = "true";
    menuToggle.addEventListener("click", () => {
      const isOpen = navMenu.classList.toggle("open");
      menuToggle.setAttribute("aria-expanded", String(isOpen));
    });
  }

  /* ── Scroll reveal via IntersectionObserver ──────────────── */
  const revealEls = document.querySelectorAll("[data-reveal], [data-reveal-group]");
  if ("IntersectionObserver" in window && revealEls.length) {
    const io = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("in-view");
            io.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.15, rootMargin: "0px 0px -40px 0px" }
    );
    revealEls.forEach((el) => io.observe(el));
  } else {
    revealEls.forEach((el) => el.classList.add("in-view"));
  }

  /* ── Animated count-up numbers ───────────────────────────── */
  const counters = document.querySelectorAll(".ph-counter[data-target]");
  if (counters.length) {
    const animateCounter = (el) => {
      const target = parseFloat(el.dataset.target);
      const suffix = el.dataset.suffix || "";
      const duration = 1400;
      const start = performance.now();
      function tick(now) {
        const progress = Math.min((now - start) / duration, 1);
        const eased = 1 - Math.pow(1 - progress, 3);
        const value = Math.floor(eased * target);
        el.textContent = value.toLocaleString() + suffix;
        if (progress < 1) requestAnimationFrame(tick);
        else el.textContent = target.toLocaleString() + suffix;
      }
      requestAnimationFrame(tick);
    };
    if ("IntersectionObserver" in window) {
      const cio = new IntersectionObserver(
        (entries) => {
          entries.forEach((entry) => {
            if (entry.isIntersecting) {
              animateCounter(entry.target);
              cio.unobserve(entry.target);
            }
          });
        },
        { threshold: 0.4 }
      );
      counters.forEach((el) => cio.observe(el));
    } else {
      counters.forEach(animateCounter);
    }
  }

  /* ── Generic accordion (FAQ pages, etc.) ─────────────────── */
  document.querySelectorAll("[data-accordion-q]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const panel = btn.nextElementSibling;
      const isOpen = panel.classList.contains("open");

      // Close siblings within the same accordion group, if specified
      const group = btn.closest("[data-accordion-group]");
      if (group) {
        group.querySelectorAll("[data-accordion-q]").forEach((otherBtn) => {
          if (otherBtn !== btn) {
            otherBtn.setAttribute("aria-expanded", "false");
            const otherPanel = otherBtn.nextElementSibling;
            otherPanel.classList.remove("open");
            otherPanel.style.maxHeight = null;
          }
        });
      }

      btn.setAttribute("aria-expanded", String(!isOpen));
      panel.classList.toggle("open", !isOpen);
      panel.style.maxHeight = !isOpen ? panel.scrollHeight + "px" : null;
    });
  });

  /* ── Tab switcher (used on Departments page) ─────────────── */
  document.querySelectorAll("[data-tabs]").forEach((tabGroup) => {
    const buttons = tabGroup.querySelectorAll("[data-tab-btn]");
    const panels = tabGroup.querySelectorAll("[data-tab-panel]");
    buttons.forEach((btn) => {
      btn.addEventListener("click", () => {
        const key = btn.dataset.tabBtn;
        buttons.forEach((b) => b.classList.toggle("active", b === btn));
        panels.forEach((p) => {
          const match = p.dataset.tabPanel === key;
          p.classList.toggle("active", match);
        });
      });
    });
  });

  /* ── Simple input focus ripple for contact/about forms ───── */
  document.querySelectorAll(".ph-input-wrap").forEach((wrap) => {
    const input = wrap.querySelector("input, textarea, select");
    if (!input) return;
    input.addEventListener("focus", () => wrap.classList.add("is-focused"));
    input.addEventListener("blur", () => wrap.classList.remove("is-focused"));
  });
})();
