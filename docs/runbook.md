# Runbook

## Local development
0) Create local env file from example and set JWT auth secrets:
```bash
cp .env.example .env
dotnet run --project src/BloodWatch.Api -- hash-password "<strong-password>"
```

1) Start services:
```bash
docker compose up -d
```

2) Open API docs:
- `http://localhost:8080/docs`
- `http://localhost:8080/app` (subscription UI; redirects to `/app/login` if not authenticated)

3) Check worker logs:
```bash
docker compose logs -f worker
```

## Troubleshooting
- If Postgres is not ready, wait for healthcheck to turn green.
- If ingestion fails, check worker logs for dador endpoint failures or mapping errors.
- If alerts are not emitted, verify subscription scope/filter values (`source`, `scopeType`, `region`/`institutionId`, `metric`) and notifier delivery errors.

## Common env vars
- `ConnectionStrings__BloodWatch`
- `BloodWatch__Worker__FetchPortugalReserves__IntervalMinutes`
- `BloodWatch__Worker__FetchPortugalReserves__ReminderIntervalHours`
- `BloodWatch__Portugal__Dador__BaseUrl`
- `BloodWatch__Portugal__Dador__BloodReservesPath`
- `BloodWatch__Portugal__Dador__InstitutionsPath`
- `BloodWatch__Portugal__Dador__SessionsPath`
- `BloodWatch__Portugal__Dador__TimeoutSeconds`
- `BloodWatch__Portugal__Dador__MaxRetries`
- `BloodWatch__Portugal__Dador__UserAgent`
- `BloodWatch__JwtAuth__Enabled`
- `BloodWatch__JwtAuth__Issuer`
- `BloodWatch__JwtAuth__Audience`
- `BloodWatch__JwtAuth__SigningKey` (JWT signing key; keep in secret manager)
- `BloodWatch__JwtAuth__AdminEmail`
- `BloodWatch__JwtAuth__AdminPasswordHash` (generate with `dotnet run --project src/BloodWatch.Api -- hash-password "<strong-password>"`)
- `BloodWatch__JwtAuth__AccessTokenMinutes`
- `BLOODWATCH__DISCORD_WEBHOOK_TIMEOUT_SECONDS`
- `BLOODWATCH__TELEGRAM_TIMEOUT_SECONDS`
- `BLOODWATCH__TELEGRAM_BOT_TOKEN`

## Notification policy
- Subscriptions are evaluated by exact `source + scopeType`.
- Supported channel types:
  - `discord:webhook`
  - `telegram:chat`
- `metric` filter modes:
  - explicit metric key: matches only that key
  - wildcard metric (`null` in API, stored as `*`): matches all metric keys within the selected scope
- Status transition rule emits events when:
  - status enters non-normal
  - status worsens
  - status recovers to normal
- Worker also emits non-normal status presence events every cycle so new matching subscriptions get an initial alert even without a new transition.
- Wildcard subscriptions emit one delivery per matched metric event (no grouped notification payload).
