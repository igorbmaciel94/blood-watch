# Security & Privacy

## Scope

BloodWatch is an informational monitoring and alerting system based on public data sources.
It must not provide medical advice.

## Secret Handling Policy

- Secrets must be provided via runtime environment variables or vault.
- Secrets must never be committed to Git.
- `.env.example` contains placeholders only.
- Production `.env` is server-only and stored outside version control.

Required sensitive values include:
- `BloodWatch__JwtAuth__SigningKey`
- `BloodWatch__JwtAuth__AdminEmail`
- `BloodWatch__JwtAuth__AdminPasswordHash`
- database credentials (`POSTGRES_*` or `ConnectionStrings__BloodWatch`)
- `BLOODWATCH__TELEGRAM_BOT_TOKEN`
- any notifier-specific credentials

## JWT Credential and Token Model

- Subscription write endpoints require JWT bearer auth.
- Tokens are issued by `POST /api/v1/auth/token` using admin email/password.
- API stores only password hash (`BloodWatch__JwtAuth__AdminPasswordHash`), never plaintext password.
- Tokens are short-lived (`BloodWatch__JwtAuth__AccessTokenMinutes`).
- Token endpoint is rate-limited.

## Runtime Configuration Guardrails

- Production startup validates required env vars and fails fast when missing.
- CI includes secret/config guard checks and leak scanning.
- Security workflows include:
  - gitleaks
  - vulnerable package checks
  - CodeQL

## Logging and Redaction

- API and Worker emit structured JSON logs.
- Correlation ID is accepted from `X-Correlation-Id` and echoed in response.
- Log fields include service/env/version/commit/correlation/job context.
- Never log:
  - JWT signing keys
  - password hashes
  - bearer tokens
  - raw webhook secrets
- Notification targets are masked in logs where applicable.

## Data Minimization

- No end-user account system in MVP.
- Subscription targets are operational endpoints/chat IDs only.
- Public reserve reads expose latest status data.

## Operational Controls

- Postgres data uses persistent storage.
- Daily backups with retention (14 days) are required.
- Restore procedure must be periodically validated.
- Runbook documents deploy, rollback, backup, and restore steps.
