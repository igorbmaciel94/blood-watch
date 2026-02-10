# ADR 0003 — No PII in v0

## Status
Accepted

## Context
Collecting personal data increases:
- security risk
- regulatory overhead
- development scope (consent, deletion, audits)

## Decision
v0 will avoid personal data:
- no user accounts
- no emails
- prefer webhooks (server-to-server)

Telegram/email may be added later with explicit opt-in and privacy updates.

## Consequences
+ Faster MVP, less risk
+ Easier open-source adoption
- Some notification channels delayed
