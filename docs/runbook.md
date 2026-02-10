# Runbook

## Local development
1) Start services:
```bash
docker compose up -d
```
2) Open Swagger:
- `http://localhost:8080/swagger` (or your configured port)

3) Check worker logs:
```bash
docker compose logs -f worker
```

## Troubleshooting
- If Postgres is not ready, wait for healthcheck to turn green.
- If ingestion fails due to schema changes, check adapter logs and update mapping.

## Common env vars
- `ConnectionStrings__BloodWatchDb`
- `BLOODWATCH__INGEST_INTERVAL_MINUTES`
- `BLOODWATCH__API_KEY` (for write endpoints)
- `BLOODWATCH__DISCORD_WEBHOOK_TIMEOUT_SECONDS`
