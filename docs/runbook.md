# Runbook

## Scope

This runbook covers local operation and production operation for the M5 deploy model:
- Single VPS
- Docker Engine + Docker Compose plugin
- Dedicated BloodWatch PostgreSQL
- Caddy TLS reverse proxy

## Server Layout

Use this filesystem layout on the VPS:

- `/opt/bloodwatch/compose/`
- `/opt/bloodwatch/data/postgres/`
- `/opt/bloodwatch/backups/`

Recommended contents of `/opt/bloodwatch/compose/`:
- `docker-compose.prod.yml`
- `Caddyfile`
- `.env`
- `scripts/deploy.sh`
- `scripts/backup-postgres.sh`
- `scripts/restore-postgres.sh`

## Local Run and Debug

1) Prepare env:

```bash
cp .env.example .env
dotnet run --project src/BloodWatch.Api -- hash-password "<strong-password>"
```

2) Start stack:

```bash
docker compose up -d --build
```

3) Start Copilot on-demand (optional):

```bash
COMPOSE_FILE=./docker-compose.yml ENV_FILE=./.env ./scripts/copilot-on.sh
```

4) Disable Copilot and release memory:

```bash
COMPOSE_FILE=./docker-compose.yml ENV_FILE=./.env ./scripts/copilot-off.sh
```

5) Inspect services:

```bash
docker compose ps
docker compose logs -f api
docker compose logs -f worker
```

6) Health checks:

- API live: `http://localhost:8080/health/live`
- API ready: `http://localhost:8080/health/ready`
- Worker live: `http://localhost:8081/health/live`
- Worker ready: `http://localhost:8081/health/ready`

## Production Initial Setup

1) Install Docker Engine + Compose plugin on VPS.

2) Create directories:

```bash
sudo mkdir -p /opt/bloodwatch/compose
sudo mkdir -p /opt/bloodwatch/data/postgres
sudo mkdir -p /opt/bloodwatch/backups
sudo chown -R "$USER":"$USER" /opt/bloodwatch
```

3) Copy deploy assets from repo:

- `deploy/docker-compose.prod.yml` -> `/opt/bloodwatch/compose/docker-compose.prod.yml`
- `deploy/Caddyfile` -> `/opt/bloodwatch/compose/Caddyfile`
- `.env.example` -> `/opt/bloodwatch/compose/.env` (then fill real values)
- `scripts/*.sh` -> `/opt/bloodwatch/compose/scripts/` (make executable)
- recommended operational shortcuts:
  - `/opt/bloodwatch/compose/scripts/copilot-on.sh`
  - `/opt/bloodwatch/compose/scripts/copilot-off.sh`

4) Configure DNS:
- Create `A` record from your production domain to VPS public IP.

5) If GHCR repository is private, authenticate on server:

```bash
docker login ghcr.io
```

## .env Requirements (Production)

At minimum set:

- `POSTGRES_DB`
- `POSTGRES_USER`
- `POSTGRES_PASSWORD`
- `BloodWatch__JwtAuth__SigningKey`
- `BloodWatch__JwtAuth__AdminEmail`
- `BloodWatch__JwtAuth__AdminPasswordHash`
- `BloodWatch__Copilot__Enabled` (`true` to enable internal Copilot)
- `BloodWatch__Copilot__AdminApiKey` (required when Copilot is enabled)
- `OLLAMA__BASE_URL`
- `OLLAMA__MODEL`
- `OLLAMA__MEM_LIMIT` (recommended on constrained hosts, example `2g`)
- `CADDY_EMAIL`
- `BLOODWATCH__TELEGRAM_BOT_TOKEN` (if Telegram delivery is enabled)

Never commit this `.env` file.

Recommended default on shared 4GB hosts:
- `BloodWatch__Copilot__Enabled=false`

Caddy host routing is fixed in [deploy/Caddyfile](../deploy/Caddyfile):
- `bloodwatch.lighthousedev.uk` -> API
- `lighthousedev.uk` and `www.lighthousedev.uk` -> redirect to `bloodwatch.lighthousedev.uk`

## Release -> Deploy Flow

### 1) Publish Release

Create a GitHub Release with tag `vX.Y.Z`.

The release workflow publishes:
- `ghcr.io/<owner>/bloodwatch-api:vX.Y.Z`
- `ghcr.io/<owner>/bloodwatch-worker:vX.Y.Z`
- `sha-<shortsha>` and `latest` tags

For non-prerelease releases, GitHub Actions automatically deploys to production through your self-hosted runner after image publish.

Required repository configuration for auto-deploy:
- one online self-hosted runner attached to this repository
- runner host must have access to `/opt/bloodwatch/compose/scripts/deploy.sh`
- optional repository variable `PROD_COMPOSE_DIR` (default `/opt/bloodwatch/compose`)

### 2) Deploy on VPS

Automatic path (default):
- no manual server command is needed after publishing a normal release.

Manual fallback path:
- run `/opt/bloodwatch/compose/scripts/deploy.sh vX.Y.Z` on the server.
- `deploy.sh` now pins `BLOODWATCH_IMAGE_TAG=vX.Y.Z` into `/opt/bloodwatch/compose/.env` to avoid accidental fallback to `latest`.

On-demand Copilot control in production (preferred via API/UI hard toggle):

```bash
curl -sS -X GET "https://bloodwatch.lighthousedev.uk/api/v1/copilot/status" \
  -H "X-Admin-Api-Key: <admin-key>"

curl -sS -X POST "https://bloodwatch.lighthousedev.uk/api/v1/copilot/feature-flag" \
  -H "X-Admin-Api-Key: <admin-key>" \
  -H "Content-Type: application/json" \
  -d '{"enabled":true}'
```

Manual fallback (server shell scripts):

```bash
COMPOSE_FILE=/opt/bloodwatch/compose/docker-compose.prod.yml ENV_FILE=/opt/bloodwatch/compose/.env /opt/bloodwatch/compose/scripts/copilot-on.sh
COMPOSE_FILE=/opt/bloodwatch/compose/docker-compose.prod.yml ENV_FILE=/opt/bloodwatch/compose/.env /opt/bloodwatch/compose/scripts/copilot-off.sh
```

What it does:
- pull API + Worker images
- run one-shot migrator
- start/update `api`, `worker`, `caddy`
- verify API/Worker readiness

## Update and Rollback

### Update to newer release

- publish next release tag (`vX.Y.Z`), auto-deploy runs automatically.

### Rollback to previous release

```bash
/opt/bloodwatch/compose/scripts/deploy.sh vX.Y.(Z-1)
```

Rollback is manual and tag-based.

## Backups

Create backup manually:

```bash
/opt/bloodwatch/compose/scripts/backup-postgres.sh
```

Backups are stored under `/opt/bloodwatch/backups/`.
Default retention is 14 days.

### Daily cron

Example cron entry:

```cron
0 3 * * * /opt/bloodwatch/compose/scripts/backup-postgres.sh >> /var/log/bloodwatch-backup.log 2>&1
```

Adjust script path to where you store it on VPS.

## Restore

Validate backup restore without modifying production DB:

```bash
/opt/bloodwatch/compose/scripts/restore-postgres.sh /opt/bloodwatch/backups/bloodwatch_YYYYMMDDTHHMMSSZ.sql.gz
```

Apply restore into production DB:

```bash
/opt/bloodwatch/compose/scripts/restore-postgres.sh /opt/bloodwatch/backups/bloodwatch_YYYYMMDDTHHMMSSZ.sql.gz --apply
```

## Merge Gate / Branch Protection

Configure GitHub branch protection for `main` with required status check:
- `pr-merge-gate / gate`

This ensures merges are blocked when required pipelines fail.

## Troubleshooting

- `migrator` fails:
  - check DB credentials in `.env`
  - check `docker compose logs migrator`
- API `503` on `/health/ready`:
  - verify `postgres` health and connectivity
- Caddy TLS not issuing:
  - verify DNS `A` record points to VPS
  - confirm ports `80/443` open
- Auth token endpoint `503`:
  - missing JWT env values in `.env`
- Copilot endpoints `503`:
  - Copilot disabled or missing `BloodWatch__Copilot__AdminApiKey`
  - Ollama unavailable or missing `OLLAMA__*` configuration
  - on 4GB hosts, use `qwen2.5:0.5b` and run `ollama` only on-demand (`--profile copilot`)
- Worker dispatch failures:
  - inspect `docker compose logs worker`
  - verify notifier secrets and webhook/chat targets
