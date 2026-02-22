# BloodWatch

BloodWatch is a C# platform to monitor public blood reserve status signals and notify subscribers when status transitions are detected.

> Disclaimer: No medical advice. Always verify donation eligibility and guidance with official health authorities.

## Milestone status

Current baseline includes:
- `BloodWatch.sln` and projects under `src/`
- Postgres baseline via EF Core migration (fresh schema for dador/IPST)
- Local development with Docker Compose (`postgres`, `api`, `worker`, `pgadmin`)
- CI workflow for restore/build/test on push + PR to `main`

## Quickstart

### 1) Start locally with Docker

Prepare auth env vars first:

```bash
cp .env.example .env
dotnet run --project src/BloodWatch.Api -- hash-password "<your-strong-password>"
```

```bash
docker compose up --build
```

### 2) Verify API

- API root: [http://localhost:8080/](http://localhost:8080/)
- Health: [http://localhost:8080/health](http://localhost:8080/health)
- OpenAPI spec: [http://localhost:8080/openapi/v1.json](http://localhost:8080/openapi/v1.json)
- API docs UI (Swagger): [http://localhost:8080/docs](http://localhost:8080/docs)
- Subscription UI (React): [http://localhost:8080/app](http://localhost:8080/app) (redirects to `/app/login` until authenticated)

Public endpoints (`source=pt-dador-ipst`):
- Sources: [http://localhost:8080/api/v1/sources](http://localhost:8080/api/v1/sources)
- Regions: [http://localhost:8080/api/v1/regions?source=pt-dador-ipst](http://localhost:8080/api/v1/regions?source=pt-dador-ipst)
- Latest reserves (status-only):
  - [http://localhost:8080/api/v1/reserves/latest?source=pt-dador-ipst](http://localhost:8080/api/v1/reserves/latest?source=pt-dador-ipst)
  - [http://localhost:8080/api/v1/reserves/latest?source=pt-dador-ipst&region=pt-norte](http://localhost:8080/api/v1/reserves/latest?source=pt-dador-ipst&region=pt-norte)
- Institutions:
  - [http://localhost:8080/api/v1/institutions?source=pt-dador-ipst](http://localhost:8080/api/v1/institutions?source=pt-dador-ipst)
  - [http://localhost:8080/api/v1/institutions?source=pt-dador-ipst&region=pt-norte](http://localhost:8080/api/v1/institutions?source=pt-dador-ipst&region=pt-norte)
  - [http://localhost:8080/api/v1/institutions/nearest?source=pt-dador-ipst&lat=38.7223&lon=-9.1393&limit=5](http://localhost:8080/api/v1/institutions/nearest?source=pt-dador-ipst&lat=38.7223&lon=-9.1393&limit=5)
- Upcoming sessions:
  - [http://localhost:8080/api/v1/sessions?source=pt-dador-ipst](http://localhost:8080/api/v1/sessions?source=pt-dador-ipst)
  - [http://localhost:8080/api/v1/sessions?source=pt-dador-ipst&region=pt-norte](http://localhost:8080/api/v1/sessions?source=pt-dador-ipst&region=pt-norte)

Auth endpoint:
- `POST /api/v1/auth/token` with body `{ "email": "...", "password": "..." }` to obtain a short-lived JWT bearer token.
- Generate an admin password hash locally:
  - `dotnet run --project src/BloodWatch.Api -- hash-password "<your-strong-password>"`

Subscription endpoints (require `Authorization: Bearer <token>`):
- `POST /api/v1/subscriptions` (`scopeType=region|institution`, `type=discord:webhook|telegram:chat`)
- `GET /api/v1/subscriptions`
- `GET /api/v1/subscriptions/{id}`
- `GET /api/v1/subscriptions/{id}/deliveries?limit=10`
- `DELETE /api/v1/subscriptions/{id}`

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
- Portugal data source: `docs/data-sources/portugal-dador-ipst.md`

## Alert behavior

- Alerts are evaluated against latest reserve status every worker cycle.
- Subscriptions require exact `source + scopeType` matching.
- `metric` is optional:
  - explicit metric matches only that metric key
  - omitted/`null` metric is stored as wildcard (`*`) and matches all metric keys in the selected scope
- Rule behavior:
  - alert when status enters non-normal
  - alert when status worsens
  - recovery alert when status returns to normal
- Worker also evaluates non-normal status presence each cycle so new matching subscriptions receive an initial alert without waiting for a new transition.
- Wildcard subscriptions still dispatch one notification per metric event (no aggregation).
