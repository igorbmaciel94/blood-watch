# BloodWatch Architecture

BloodWatch is a **backend-first**, open-source service that:
- Ingests **public blood reserve data** from a country/region via **adapters** (start with Portugal)
- Stores an auditable **history** in Postgres
- Exposes a stable **OpenAPI** HTTP API
- Sends **alerts** via pluggable notifiers (Discord/Telegram/Webhook → email later)
- Avoids clinical decision-making and minimizes personal data

> ⚠️ Disclaimer: BloodWatch is **not medical advice**. It reports public data and emits informational alerts. Always verify eligibility and donation guidance with official sources.

---

## Tech stack (initial)

- **.NET 8**
- **PostgreSQL** (dev + prod) + EF Core migrations
- **Docker + docker-compose** for local dev
- **OpenAPI/Swagger** from ASP.NET Minimal API
- **CI**: GitHub Actions (build/test/lint, optional docker publish)

Optional later:
- Minimal **Front-end** for subscription management + viewing trends
- Stronger auth (JWT/OAuth) if needed

---

## Repository layout

```
BloodWatch/
  src/
    BloodWatch.Api/                 # Minimal API + Swagger
    BloodWatch.Worker/              # Background jobs (ingest + rules + dispatch)
    BloodWatch.Core/                # Domain models + contracts (adapters/rules/notifiers)
    BloodWatch.Infrastructure/      # Postgres, EF Core, HTTP, messaging utils
    BloodWatch.Adapters.Portugal/   # First adapter (Portugal open data)
    BloodWatch.Notifiers.Discord/   # Discord webhook notifier
    BloodWatch.Notifiers.Telegram/  # Telegram notifier (later)
    BloodWatch.Notifiers.Email/     # Email notifier (later)
  tests/
    BloodWatch.Tests/
  ops/
    docker/
    compose/
  docs/
    architecture.md
    adapter-howto.md
    runbook.md
    security.md
    adr/
  .github/workflows/
  docker-compose.yml
  README.md
```

---

## Local dev: Docker & Postgres

### docker-compose.yml (dev)
- `postgres` container with persistent volume
- `api` container
- `worker` container

Dev preference:
- Use Postgres for everything (no SQLite) so migrations and queries stay consistent.
- SQLite can be used only for unit tests if desired, but it’s not required.

---

## Data model (v0)

### Canonical domain
- **Snapshot**: `source`, `captured_at_utc`, `reference_date` (if dataset provides), `items[]`
- **Item**: `metric`, `region`, `value`, `unit`, optional `severity`

### Postgres tables (suggestion)
- `sources` (id, name, adapter_key)
- `regions` (id, source_id, key, display_name)
- `snapshots` (id, source_id, captured_at_utc, reference_date, hash)
- `snapshot_items` (snapshot_id, metric, region_id, value, unit, severity)
- `subscriptions` (id, type, target, source_id, region_filter, is_enabled, created_at)
- `events` (id, source_id, snapshot_id, rule_key, region_id, metric, payload_json, created_at)
- `deliveries` (id, event_id, subscription_id, status, last_error, created_at)

Notes:
- `target` can be a Discord webhook URL, generic webhook URL, or later a Telegram chat id/email.
- Keep payload minimal; avoid personal details.

---

## Contracts (core)

### IDataSourceAdapter
- `AdapterKey` (string)
- `GetAvailableRegionsAsync()`
- `FetchLatestAsync()` returns `Snapshot`

### IRule
- `RuleKey`
- `Evaluate(previousSnapshot, currentSnapshot) => events[]`

### INotifier
- `TypeKey`
- `Send(event, subscription)`

---

## Architecture diagram (Mermaid)

```mermaid
flowchart LR
  subgraph Sources
    PT[Portugal Open Data\n(reserves dataset)]
    ES[Future: Spain Adapter]
    BR[Future: Brazil Adapter]
  end

  subgraph Worker[BloodWatch.Worker]
    Sch[Scheduler]\n(weekly+hourly)
    Ing[Ingestion Engine]
    Rules[Rule Engine]
    Disp[Dispatch Engine]
  end

  subgraph Core[BloodWatch.Core]
    Adapters[IDataSourceAdapter]
    Canon[Canonical Models]
    RulesIf[IRule]
    Notif[INotifier]
  end

  subgraph Infra[BloodWatch.Infrastructure]
    Pg[(Postgres)]
    Repo[Repositories]
  end

  subgraph API[BloodWatch.Api]
    Endpoints[OpenAPI REST\n/latest /trend /regions\n/subscriptions]
  end

  subgraph Notifiers
    Discord[Discord Webhook]
    Telegram[Telegram]
    Email[Email]
    Webhook[Generic Webhook]
  end

  PT --> Ing
  ES -.-> Ing
  BR -.-> Ing

  Sch --> Ing
  Ing --> Repo --> Pg
  Repo --> Endpoints

  Ing --> Rules --> Repo
  Rules --> Disp
  Disp --> Discord
  Disp --> Telegram
  Disp --> Email
  Disp --> Webhook

  Endpoints --> Repo
```
---

## OpenAPI-first endpoints (v0)

### Read
- `GET /api/v1/sources`
- `GET /api/v1/regions?source=pt`
- `GET /api/v1/reserves/latest?source=pt`
- `GET /api/v1/reserves/trends?source=pt&days=30&region=<key>&metric=<metric>`
- `GET /api/v1/events?source=pt&days=14`

### Write (subscriptions)
- `POST /api/v1/subscriptions`
- `GET /api/v1/subscriptions/{id}`
- `DELETE /api/v1/subscriptions/{id}` (soft-delete optional)

Auth plan:
- **Phase 0:** no auth for read endpoints; write endpoints protected by an **API key**.
- **Phase 1:** optional JWT + basic UI.

---

## DevOps plan (GitHub)

### CI (GitHub Actions)
- Restore/build/test
- Optional `dotnet format` / lint checks
- Build docker images (optional)

### CD (optional)
- Deploy to a simple host (Render/Fly.io/Azure/etc.) later
- For now: local + GitHub Container Registry (optional)

---

## Privacy & risk boundaries

- Do not provide medical advice or eligibility decisions.
- Prefer webhooks and chat IDs; avoid collecting emails in v0.
- Document how to delete/disable subscriptions and purge stored records.
- Rate-limit public endpoints.

---

## Next decisions

- Choose deployment host later (don’t block MVP).
- Add Telegram/Email/UI after core pipeline is stable.
