# BloodWatch

BloodWatch is a C# platform to monitor public blood reserve status signals and notify subscribers when status transitions are detected.

> Disclaimer: No medical advice. Always verify donation eligibility and guidance with official health authorities.

## MVP Operations Status (M5)

- API + Worker run in Docker with PostgreSQL persistence.
- API serves the React subscription console at `/app`.
- Structured JSON logs are enabled with correlation scopes.
- Health contract is standardized:
  - `GET /health/live`
  - `GET /health/ready`
  - `GET /health` (compatibility alias)
  - `GET /version`
- Release workflow publishes versioned images to GHCR on GitHub Release.
- Production deployment targets a single VPS using Docker Compose + Caddy TLS.

## Quickstart (Local)

1) Prepare environment:

```bash
cp .env.example .env
dotnet run --project src/BloodWatch.Api -- hash-password "<your-strong-password>"
```

2) Start local stack:

```bash
docker compose up --build
```

This starts `postgres`, `migrator`, `api`, `worker`, and `pgadmin`.

## Local URLs

- API root: [http://localhost:8080/](http://localhost:8080/)
- API docs: [http://localhost:8080/docs](http://localhost:8080/docs)
- API OpenAPI: [http://localhost:8080/openapi/v1.json](http://localhost:8080/openapi/v1.json)
- Subscription UI: [http://localhost:8080/app](http://localhost:8080/app)
- API liveness: [http://localhost:8080/health/live](http://localhost:8080/health/live)
- API readiness: [http://localhost:8080/health/ready](http://localhost:8080/health/ready)
- API version: [http://localhost:8080/version](http://localhost:8080/version)
- Worker root: [http://localhost:8081/](http://localhost:8081/)
- Worker liveness: [http://localhost:8081/health/live](http://localhost:8081/health/live)
- Worker readiness: [http://localhost:8081/health/ready](http://localhost:8081/health/ready)
- Worker version: [http://localhost:8081/version](http://localhost:8081/version)
- pgAdmin: [http://localhost:5050](http://localhost:5050)

## API Access Model

- Public read endpoints remain rate limited.
- Subscription endpoints require `Authorization: Bearer <token>`.
- Token issuance endpoint:
  - `POST /api/v1/auth/token`
  - Body: `{ "email": "...", "password": "..." }`

## Production Deployment Model

- Deployment model: direct Docker Compose on VPS.
- Reverse proxy/TLS: Caddy with automatic certificates.
- Production startup requires explicit build metadata envs (`BloodWatch__Build__Version`, `BloodWatch__Build__Commit`, `BloodWatch__Build__Date`).
- Compose assets:
  - [deploy/docker-compose.prod.yml](deploy/docker-compose.prod.yml)
  - [deploy/Caddyfile](deploy/Caddyfile)
- Operational scripts:
  - [scripts/deploy.sh](scripts/deploy.sh)
  - [scripts/backup-postgres.sh](scripts/backup-postgres.sh)
  - [scripts/restore-postgres.sh](scripts/restore-postgres.sh)

See [docs/runbook.md](docs/runbook.md) for end-to-end production procedures.

## Release Pipeline

GitHub Release (`published`) triggers image publishing to GHCR via:
- [release-images workflow](.github/workflows/release-images.yml)

For non-prerelease releases, the same workflow also deploys automatically on your self-hosted production runner.

Published tags:
- `vX.Y.Z`
- `sha-<shortsha>`
- `latest`

## Required Checks

PR merges are blocked by `pr-merge-gate / gate` when required pipelines fail.

## Repository Layout

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
deploy/
scripts/
```

## Documentation

- Architecture: [docs/architecture.md](docs/architecture.md)
- Adapter guide: [docs/adapter-howto.md](docs/adapter-howto.md)
- Data source reference: [docs/data-sources/portugal-dador-ipst.md](docs/data-sources/portugal-dador-ipst.md)
- Runbook: [docs/runbook.md](docs/runbook.md)
- Security: [docs/security.md](docs/security.md)
