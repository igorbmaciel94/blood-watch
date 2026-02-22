# Security & Privacy

## Scope
BloodWatch is an informational monitoring and alerting system built on public data sources.
It must not provide medical advice.

## Data minimization (v0)
- No end-user account model (single admin credential for subscription management)
- Subscriptions should use:
  - webhooks, or
  - Telegram chat IDs

## API protection
- Read endpoints can be public (rate-limited)
- Write endpoints must require JWT bearer authentication
- Tokens are issued by `POST /api/v1/auth/token` using admin email/password credentials and are short-lived
- Store only `BloodWatch__JwtAuth__AdminPasswordHash` in env/vault, never plain passwords
- Validate webhook URLs and prevent SSRF where possible

## Logging
- Avoid logging full webhook URLs; mask sensitive parts
- Never log secrets (passwords, password hashes, JWT signing keys, tokens)

## Deletion
- Provide a way to disable/delete subscriptions
- Keep public reserve reads latest-state and auditable through operational event logs
