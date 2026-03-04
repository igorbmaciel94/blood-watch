# ADR 0002 — Copilot external knowledge POC (Open Crawl compatible)

## Status
Proposed

## Context
Copilot currently answers from BloodWatch internal analytics only.
Some operational questions can benefit from official external health sources (for example, policy notices or campaign pages), but introducing web ingestion directly in the request path increases latency, security risk, and operational complexity.

We want a POC architecture that is vendor-neutral (compatible with Open Crawl/Open Crawler style ingestion), safe by default, and isolated from production request reliability.

## Decision
Implement a POC architecture with these constraints:

1. Asynchronous ingestion pipeline only
- External crawling/fetching runs out-of-band (scheduled job/worker), never inside `/api/v1/copilot/ask`.
- Copilot request path remains internal-data-first and low-latency.

2. Strict source allowlist
- Only official health domains are eligible in the POC allowlist.
- Initial allowlist:
  - `ipst.pt`
  - `sns24.gov.pt`
  - `dgs.pt`
- No open web crawling beyond explicit allowlist entries.

3. Versioned persistence for external documents
- Store normalized documents with:
  - source URL
  - domain
  - fetched timestamp
  - canonical content hash (sha256)
  - parser version
  - extraction metadata
- Keep immutable versions to support traceability and rollback.

4. Feature flag disabled by default
- External knowledge usage is controlled by a dedicated feature flag.
- Default value is `false` in all environments.
- Enabling is explicit and environment-scoped.

5. No third-party credentials in API runtime
- API runtime remains free of crawler vendor credentials.
- If credentials are needed for the ingestion job, store them only in that job's runtime boundary.

## Consequences
+ Enables incremental evaluation of external knowledge quality and risk.
+ Keeps production Copilot request path stable while testing ingestion.
+ Preserves future portability across crawler implementations.
- Adds data lifecycle responsibilities (retention, deduplication, provenance auditing).
- Requires separate observability for ingestion job health and freshness.
