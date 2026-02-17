# BloodWatch

BloodWatch is a C# platform to monitor public blood inventory signals and notify subscribers when rules trigger.

> Disclaimer: No medical advice. Always verify donation eligibility and guidance with official health authorities.

## Milestone M0 status

M0 foundation includes:
- `BloodWatch.sln` and core projects under `src/`
- Postgres baseline via EF Core migration + seed data for Portugal source
- Local development with Docker Compose (`postgres`, `api`, `worker`, `pgadmin`)
- CI workflow for restore/build/test on push + PR to `main`

## Quickstart

### 1) Start locally with Docker

```bash
docker compose up --build
```

### 2) Verify API

- API root: [http://localhost:8080/](http://localhost:8080/)
- Health: [http://localhost:8080/health](http://localhost:8080/health)
- OpenAPI spec: [http://localhost:8080/openapi/v1.json](http://localhost:8080/openapi/v1.json)
- API docs UI (Swagger): [http://localhost:8080/docs](http://localhost:8080/docs)

### 3) Verify Worker Health

- Worker root: [http://localhost:8081/](http://localhost:8081/)
- Worker liveness: [http://localhost:8081/health](http://localhost:8081/health)
- Worker readiness (DB connectivity): [http://localhost:8081/health/ready](http://localhost:8081/health/ready)
- Worker OpenAPI spec: [http://localhost:8081/openapi/v1.json](http://localhost:8081/openapi/v1.json)
- Worker docs UI (Swagger): [http://localhost:8081/docs](http://localhost:8081/docs)

### 4) Manage database in pgAdmin

- URL: [http://localhost:5050](http://localhost:5050)
- Default login email: `admin@bloodwatch.com`
- Default login password: `admin`

Add a server in pgAdmin with:
- Host: `postgres`
- Port: `5432`
- Database: `bloodwatch`
- Username: `bloodwatch`
- Password: `bloodwatch`

## Repository layout

```text
src/
  BloodWatch.Core/
  BloodWatch.Infrastructure/
  BloodWatch.Api/
  BloodWatch.Worker/
  BloodWatch.Adapters.Portugal/
tests/
  BloodWatch.Core.Tests/
docs/
.github/workflows/
```

## Documentation

- Architecture: `docs/architecture.md`
- Adapter guide: `docs/adapter-howto.md`
- Portugal data source: `docs/data-sources/portugal-reservas.md`
