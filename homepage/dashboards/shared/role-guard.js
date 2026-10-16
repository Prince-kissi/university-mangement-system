(function () {
  "use strict";

  const validRoles = ["department_officer", "admin", "super_admin"];
  const requiredRole = document.documentElement.dataset.requiredRole;

  if (!requiredRole || !validRoles.includes(requiredRole)) {
    console.error("Dashboard role guard is missing a valid required role.");
    location.replace("../../admin-login.html?reason=role-configuration");
    return;
  }

  // Authentication and role authorization are verified by /api/auth/me and
  // every protected backend endpoint. URL parameters and local storage are
  // intentionally never trusted for access decisions.
})();
