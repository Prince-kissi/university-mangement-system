# UMaT Complaint Management development startup

## Start everything

With Node.js installed:

```powershell
npm run dev
```

On Windows without Node.js:

```powershell
.\Start-UMaT.ps1
```

Both commands build the API, start PostgreSQL, Redis, the API, Nginx frontend, and Adminer with Docker Compose, wait for the required health checks, and then open the homepage.

Do not open `homepage.html` as a `file:///` URL. Use `http://localhost:8080/homepage.html`.

## Environment

Copy the root `.env.example` to `.env` to override frontend development URLs:

```text
UMAT_API_BASE_URL=http://localhost:8080
UMAT_FRONTEND_URL=http://localhost:8080/homepage.html
UMAT_DATABASE_HEALTH_URL=http://localhost:8080/api/database/health
UMAT_AUTH_HEALTH_URL=http://localhost:8080/api/auth/health
```

Copy `backend/.env.example` to `backend/.env` and replace every placeholder:

```text
POSTGRES_DB=umat_complaints
POSTGRES_USER=umat_app
POSTGRES_PASSWORD=<strong unique database password>
REDIS_PASSWORD=<different strong Redis password>
ANONYMOUS_TRACKING_PEPPER=<at least 32 random characters>
SUPER_ADMIN_EMAIL=<UMaT administrator email>
SUPER_ADMIN_PASSWORD=<strong one-time bootstrap password>
FRONTEND_URL=http://localhost:8080
SMTP_HOST=<SMTP host, or blank for local testing>
SMTP_PORT=587
SMTP_USERNAME=<SMTP username, or blank for local testing>
SMTP_PASSWORD=<SMTP password, or blank for local testing>
SMTP_FROM=no-reply@umat.edu.gh
```

`Database__UseInMemory=false` is enforced by Docker Compose, so development authentication uses PostgreSQL rather than temporary fake data.

## Verify the stack

```powershell
npm run dev:status
```

These URLs must return HTTP 200:

- `http://localhost:8080/api/health`
- `http://localhost:8080/api/auth/health`
- `http://localhost:8080/api/database/health`

Follow service logs with:

```powershell
npm run dev:logs
```

Open the local database viewer at `http://127.0.0.1:8081`.

Stop the development stack with:

```powershell
npm run dev:stop
```
