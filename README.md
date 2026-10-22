# UMaT Student Complaint Management System

A web-based complaint management platform for the University of Mines and Technology (UMaT). The system provides a central place for students to submit and track complaints and for authorized university staff to manage and respond to them.

## Features

- **Student accounts** — registration, email verification, login, and password reset.
- **Complaint submission** — submit complaints through the web interface and track their progress.
- **Anonymous complaints** — support for submitting and tracking complaints anonymously.
- **Complaint tracking** — follow complaint status using the tracking workflow.
- **Administrative access** — dedicated administrator and department-officer account pages.
- **Security controls** — role-based authorization, CSRF protections, rate limiting, secure authentication workflows, and authenticator enrolment for privileged accounts.
- **Email and SMS integration** — configurable verification, password-reset, and OTP messaging integrations.
- **Operational monitoring** — API, authentication, and database health endpoints.
- **Reporting and operations** — backend support for reports, backups, and evidence storage.

> Feature availability depends on the deployed configuration and external services.

## Technology Stack

**Frontend**
- HTML
- CSS
- JavaScript

**Backend**
- ASP.NET Core Web API (.NET 10)
- ASP.NET Core Identity
- Entity Framework Core

**Data and infrastructure**
- PostgreSQL
- Redis
- Docker Compose
- Nginx

**Additional tooling**
- Swagger / OpenAPI
- FluentValidation
- Serilog

## Architecture

The backend follows a layered architecture:

- `ComplaintSystem.Domain` — domain entities and business rules.
- `ComplaintSystem.Application` — application contracts, service abstractions, validation, permissions, and complaint workflow rules.
- `ComplaintSystem.Infrastructure` — persistence, identity, Redis, messaging, storage, reporting, and other service implementations.
- `ComplaintSystem.Api` — HTTP endpoints, middleware, security controls, Swagger, and health checks.
- `ComplaintSystem.UnitTests` and `ComplaintSystem.IntegrationTests` — automated test projects.

The existing HTML, CSS, JavaScript, and image assets provide the web interface, while the ASP.NET Core API handles backend operations.

## Requirements

For the Docker-based setup:

- Git
- Docker Desktop with Docker Compose
- A modern web browser

For running the backend without Docker, also install the .NET 10 SDK, PostgreSQL 17 or later, and Redis 7 or later.

SMTP and SMS provider credentials are needed to test real email and SMS delivery. You can use sandbox/test settings where supported.

## Getting Started (Docker)

### 1. Clone the repository

```bash
git clone <university-mangement-system>
cd <university-mangement-system>
```

Run the commands from the project directory containing `package.json` and the `backend/` folder.

### 2. Configure environment variables

Copy the example environment files:

**Windows PowerShell**
```powershell
Copy-Item .env.example .env
Copy-Item backend/.env.example backend/.env
```

**macOS / Linux**
```bash
cp .env.example .env
cp backend/.env.example backend/.env
```

Edit `backend/.env` and replace every placeholder with unique, strong values. In particular, set:

- PostgreSQL database credentials
- Redis password
- Anonymous tracking pepper (at least 32 random characters)
- Initial Super Admin email and one-time password
- SMTP credentials, if testing email
- SMS provider credentials, if testing SMS

Never commit `.env` files or real credentials to GitHub. Use `.env.example` for placeholders only.

### 3. Start the application

From the project root:

```bash
docker compose --file backend/docker-compose.yml up --build
```

On Windows, the included startup scripts can also be used:

```powershell
npm run dev
```

or:

```powershell
.\Start-UMaT.ps1
```

### 4. Open the application

- **Web application:** http://localhost:8080/homepage.html
- **API health:** http://localhost:8080/api/health
- **Authentication health:** http://localhost:8080/api/auth/health
- **Database health:** http://localhost:8080/api/database/health
- **Database viewer (Adminer):** http://127.0.0.1:8081

Swagger is available at `http://localhost:8080/swagger` when the API is running in the Development environment.

Do not open `homepage.html` directly using a `file:///` URL. Use the local HTTP address above so the frontend can communicate with the API.

### 5. Check service status and logs

```bash
npm run dev:status
npm run dev:logs
```

Stop the local stack with:

```bash
npm run dev:stop
```

## Initial Administrator Setup

The first Super Admin is bootstrapped only when no Super Admin already exists and the bootstrap email and password are configured. Use a strong, temporary password and change it immediately after setup. Remove the bootstrap password from the deployment environment after initialization. Follow the application's authenticator-enrolment flow for privileged accounts.

## Configuration Notes

The Docker Compose setup is intended for local development. Before deploying publicly:

- Use HTTPS and set the production HTTPS requirement.
- Use a real hostname and restrict allowed frontend origins.
- Store secrets in your hosting platform's secret manager or environment configuration.
- Do not expose PostgreSQL, Redis, or internal API ports directly to the internet.
- Configure SMTP and SMS providers and verify sender identities.
- Review backup, access-control, logging, and data-retention policies.
- Set the ASP.NET Core environment to `Production`.

See [`DEVELOPMENT.md`](DEVELOPMENT.md) and [`backend/README.md`](backend/README.md) for additional development and backend instructions.

## Testing

The backend contains unit and integration test projects. From the `backend/` directory, restore the solution and run:

```bash
dotnet restore ComplaintSystem.sln
dotnet test ComplaintSystem.sln
```

Some integration tests may require supporting services or test-specific configuration.

## Contributing

Contributions, bug reports, and suggestions are welcome.

1. Fork the repository.
2. Create a branch for your change.
3. Make and test your changes.
4. Open a pull request describing the change.

Please do not include passwords, API keys, personal information, or real complaint records in issues, commits, or test fixtures.

## Security

If you discover a security issue, please avoid publishing exploit details or sensitive data in a public issue. Contact the project maintainers privately.

## License

No top-level project license was identified in the supplied project files. Add a `LICENSE` file before specifying reuse or redistribution terms.
