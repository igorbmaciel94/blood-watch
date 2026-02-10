# BloodWatch

BloodWatch is a global, adapter-based service that ingests public blood reserve data, stores history in Postgres, exposes an OpenAPI API, and sends alerts via Discord/webhooks.

> Not medical advice. Always verify donation guidance with official sources.

## Quickstart (local)
```bash
docker compose up -d
# Swagger:
# http://localhost:8080/swagger
```

## Docs
- Architecture: `docs/architecture.md`
- Adapter guide: `docs/adapter-howto.md`
- Runbook: `docs/runbook.md`
- Security: `docs/security.md`
