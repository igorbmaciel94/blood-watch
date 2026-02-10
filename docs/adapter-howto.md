# Adapter How-To

This guide explains how to add a new data source adapter (country/region) to BloodWatch.

## 1) Create a new adapter project
Create a project like:
- `BloodWatch.Adapters.Spain`
- `BloodWatch.Adapters.Brazil`

Reference `BloodWatch.Core` and `BloodWatch.Infrastructure`.

## 2) Implement `IDataSourceAdapter`
Your adapter should:
- expose a stable `AdapterKey` (e.g., `pt-transparencia-sns`)
- fetch data from the public source
- map it into canonical `Snapshot` + `SnapshotItem` models
- normalize region keys and metric keys

## 3) Add configuration
Adapters should read:
- base URLs
- API keys (if any)
- schedule hints (optional)

from configuration / environment variables.

## 4) Register the adapter
In the Worker, register adapters using DI (Dependency Injection).
The ingestion job should run adapters by adapter key.

## 5) Add tests
Add unit tests with stored JSON samples:
- mapping correctness
- null/format tolerance
- dedupe stability (if adapter creates snapshot hash inputs)

## 6) Document your source
Add a short section in `docs/data-sources.md` describing:
- source link
- refresh cadence
- key fields used
- any limitations
