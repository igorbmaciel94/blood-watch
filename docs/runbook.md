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
- `ConnectionStrings__BloodWatch`
- `BloodWatch__Worker__FetchPortugalReserves__IntervalMinutes`
- `BloodWatch__Alerts__BaseCriticalUnits`
- `BloodWatch__Alerts__WarningMultiplier`
- `BloodWatch__Alerts__CriticalStepDownPercent`
- `BloodWatch__Alerts__ReminderIntervalHours` (default `24`)
- `BloodWatch__Alerts__WorseningBucketDelta` (default `1`)
- `BloodWatch__Alerts__SendRecoveryNotification` (default `true`)
- `BloodWatch__Portugal__TransparenciaSns__TimeoutSeconds`
- `BloodWatch__Portugal__TransparenciaSns__MaxRetries`
- `BloodWatch__Portugal__TransparenciaSns__UserAgent`
- `BLOODWATCH__API_KEY` (for write endpoints)
- `BLOODWATCH__DISCORD_WEBHOOK_TIMEOUT_SECONDS`

## Notification policy
- Subscriptions are evaluated by exact `source + region + metric`.
- Critical notifications are rate-limited:
  - first critical alert immediately
  - daily reminder while still critical
  - immediate alert when critical bucket worsens
  - optional recovery notification on exit from critical
