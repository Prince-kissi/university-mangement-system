# UMaT Student Complaint Management Backend

ASP.NET Core 10 Web API for the existing University of Mines and Technology complaint-management frontend. The backend uses a clean four-project architecture, PostgreSQL, ASP.NET Core Identity, Redis-backed sessions, FluentValidation, Serilog, Swagger/OpenAPI, Docker and Nginx.

The existing HTML, CSS, JavaScript and image files outside this `backend` directory are not redesigned or replaced.

## Architecture

- `ComplaintSystem.Domain`: entities, enums and persistence-neutral domain rules.
- `ComplaintSystem.Application`: API contracts, service abstractions, permissions, validators and complaint workflow rules.
- `ComplaintSystem.Infrastructure`: Identity, EF Core/PostgreSQL, Redis sessions, authorization, storage, email, reports, backups, seeding and service implementations.
- `ComplaintSystem.Api`: HTTP controllers, cookies, CSRF, rate limiting, middleware, Swagger and health endpoints.
- `ComplaintSystem.UnitTests`: email-policy and complaint-workflow tests.
- `ComplaintSystem.IntegrationTests`: registration, verification, login, complaint submission and anonymous-tracking workflows.

## Prerequisites

- .NET 10 SDK
- PostgreSQL 17+
- Redis 7+
- `pg_dump` and `pg_restore` for backup operations
- An SMTP account for verification and password-reset email
- Docker Desktop and Docker Compose if using the container setup

## Docker quick start

1. Copy `.env.example` to `.env`.
2. Replace every placeholder with a unique secret. Do not reuse the PostgreSQL password, Redis password, tracking pepper or Super Admin password.
3. Start the stack:

```powershell
docker compose up --build
```

4. Open `http://localhost:8080/homepage.html`.
5. Open Swagger at `http://localhost:8080/swagger` only when the API is running in `Development`.

The compose configuration is for local development and sets `ASPNETCORE_ENVIRONMENT=Development`. For production, set it to `Production`, terminate TLS at Nginx or a trusted ingress, set `Security__RequireHttps=true`, use a real hostname, restrict the CORS allowlist, and do not publish PostgreSQL, Redis or the API container directly.

When no Super Administrator exists, open `http://localhost:8080/create-super-admin.html` to complete the one-time database-backed setup. The setup endpoint closes after the first account is created. Admin and Super Admin accounts must enrol an authenticator before their first session is issued.

## Local setup without Docker

Set configuration through environment variables or .NET user-secrets. Double underscores map nested configuration keys.

```powershell
$env:ConnectionStrings__PostgreSql='Host=localhost;Port=5432;Database=umat_complaints;Username=umat_app;Password=YOUR_SECRET'
$env:ConnectionStrings__Redis='localhost:6379,password=YOUR_OTHER_SECRET,abortConnect=false'
$env:Security__AnonymousTrackingPepper='AT_LEAST_32_RANDOM_CHARACTERS'
$env:Frontend__BaseUrl='https://localhost:7048'
$env:Smtp__Host='smtp.example.edu.gh'
$env:Smtp__Username='YOUR_SMTP_USER'
$env:Smtp__Password='YOUR_SMTP_SECRET'
```

Restore, migrate, seed and run:

```powershell
dotnet restore ComplaintSystem.sln
dotnet tool restore
dotnet ef database update --project src/ComplaintSystem.Infrastructure --startup-project src/ComplaintSystem.Api
$env:Database__MigrateOnStartup='true'
dotnet run --project src/ComplaintSystem.Api
```

`Database__MigrateOnStartup=true` applies pending migrations and runs the idempotent seed process. In production, prefer a controlled migration job and leave this option false on ordinary API replicas.

Create a new migration after an intentional model change:

```powershell
dotnet ef migrations add DescriptiveMigrationName --project src/ComplaintSystem.Infrastructure --startup-project src/ComplaintSystem.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/ComplaintSystem.Infrastructure --startup-project src/ComplaintSystem.Api
```

The application does not seed demo users. Accounts must be created through the real registration or authorised administration APIs.

## Frontend connection

Nginx serves the existing frontend and proxies relative `/api/...` requests to ASP.NET Core. Cookies therefore stay same-origin. Every state-changing request must first obtain an antiforgery token and send it in `X-CSRF-TOKEN`; `credentials: "include"` is required.

```javascript
async function apiRequest(path, options = {}) {
  const csrf = await fetch('/api/auth/csrf', { credentials: 'include' }).then(r => r.json());
  return fetch(`/api${path}`, {
    ...options,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': csrf.token,
      ...(options.headers || {})
    }
  });
}

await apiRequest('/auth/login', {
  method: 'POST',
  body: JSON.stringify({ identifier: emailOrReferenceNumber, password, rememberMe: false })
});
```

Do not store roles, session identifiers, passwords, reset tokens or anonymous tracking codes in localStorage. The server derives roles and departments from Identity and validates the active session for every authenticated request.

## Principal API routes

| Area | Routes |
|---|---|
| Authentication | `/api/auth/register`, `/login`, `/verify-email`, `/forgot-password`, `/reset-password`, `/two-factor/*`, `/me`, `/profile`, `/change-password`, `/logout` |
| Complaints | `/api/complaints`, `/{id}`, `/{id}/assign`, `/{id}/status`, `/{id}/priority`, `/{id}/responses`, `/{id}/internal-notes` |
| Anonymous | `/api/anonymous-complaints`, `/track`, `/responses`, `/{complaintId}/evidence` |
| Evidence | `/api/evidence/complaint/{complaintId}`, `/{id}`, `/{id}/download` |
| Administration | `/api/users`, `/api/departments`, `/api/categories` |
| Notifications | `/api/notifications`, `/api/notifications/announcements` |
| Reporting | `/api/reports/summary`, `/api/reports/export/{csv|xlsx|pdf}` |
| Audit | `/api/audit-logs` (read-only) |
| Super Admin | `/api/system/settings`, `/sessions`, `/backups`, `/health` |
| Platform health | `/health/live`, `/health/ready` |

Department Officer access is constrained again in the service layer, not merely by controller attributes. An officer cannot read, respond to, assign or access evidence for a complaint in another department. Admin and Super Admin cross-department access depends on stored role permissions.

## Evidence security

- Allowed formats: PDF, PNG, JPEG, WebP and DOCX.
- Extension, declared MIME type and magic bytes/container structure must agree.
- Files are copied through a hard size limit, renamed with generated identifiers, hashed with SHA-256 and stored outside the public web root.
- Metadata remains in PostgreSQL. View and download events create immutable access and audit records.
- There is no permanent-delete endpoint for Department Officers; the available operation is soft deletion.
- `IMalwareScanner` is the replacement point for ClamAV or a managed scanner. The supplied implementation performs no external antivirus scan, so production deployment should replace it before accepting untrusted files.

## Anonymous tracking

Anonymous submission returns the raw tracking code exactly once. Only an HMAC-SHA-256 hash is stored. The server-side pepper comes from environment configuration. Verification uses constant-time comparison and locks tracking temporarily after repeated failures.

## Backups

Backup files are generated by `pg_dump` outside the web root. Restore uses `pg_restore` with argument lists rather than a shell. The API never returns a connection string or database password. Restore requires all of the following:

- the `SuperAdmin` role and `backups.manage` permission;
- current-password confirmation;
- enabled and valid authenticator 2FA;
- a written reason of at least 20 characters;
- a completed backup record.

Backups are not a substitute for encrypted off-site backups and tested disaster-recovery procedures. Limit filesystem access to the API service account and encrypt the backup volume.

## Testing

```powershell
dotnet build ComplaintSystem.sln --configuration Release
dotnet test ComplaintSystem.sln --configuration Release --no-build
```

Integration tests use EF Core's isolated in-memory provider, an in-memory distributed cache and a fake email sender. Production behavior must also be verified against PostgreSQL and Redis in a staging environment.

## Security and operations checklist

- Put Nginx behind valid TLS and enable HSTS only after HTTPS is correct.
- Keep PostgreSQL and Redis on a private network; require strong credentials and encrypted connections where supported.
- Store environment secrets in a deployment secret manager, never in `appsettings*.json`, source control or frontend JavaScript.
- Persist and protect Data Protection keys; all replicas must share the same protected key ring.
- Configure SMTP, replace the placeholder malware scanner, and verify email deliverability.
- Forward client IP/protocol only from trusted proxies. Configure known proxy/network entries for the production topology.
- Monitor `/health/ready`, Serilog output, failed authentication, overdue complaints, backup failures and disk capacity.
- Review retention rules with UMaT governance and Ghana's data-protection requirements before production use.
- Run `dotnet list package --outdated` regularly, review release notes, update one dependency group at a time, then rebuild and run all tests. Use Dependabot or Renovate in the eventual source repository.
