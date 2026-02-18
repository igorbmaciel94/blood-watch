# Security & Privacy

## Scope
BloodWatch is an informational monitoring and alerting system built on public data sources.
It must not provide medical advice.

## Data minimization (v0)
- No user accounts
- No email addresses in v0
- Subscriptions should use:
  - webhooks, or
  - Telegram chat IDs (later)

## API protection
- Read endpoints can be public (rate-limited)
- Write endpoints must require an API key
- Validate webhook URLs and prevent SSRF where possible

## Logging
- Avoid logging full webhook URLs; mask sensitive parts
- Never log secrets (API keys, tokens)

## Deletion
- Provide a way to disable/delete subscriptions
- Keep public reserve reads latest-state and auditable through operational event logs
