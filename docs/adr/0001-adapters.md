# ADR 0001 — Adapter-based architecture

## Status
Accepted

## Context
Blood-related public data sources vary by country/region:
- different schemas
- different refresh cadences
- different region/metric naming

We want a stable core pipeline (ingest → store → API → alerts) that does not change when adding new sources.

## Decision
Use an adapter interface (`IDataSourceAdapter`) per country/region. Adapters:
- fetch source data
- map to canonical models
- normalize keys

Core remains source-agnostic.

## Consequences
+ Easy to add sources without touching core
+ Keeps mapping complexity isolated
- Requires discipline in canonical modeling
