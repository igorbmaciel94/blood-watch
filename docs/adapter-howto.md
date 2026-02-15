# Adapter How-To

This guide explains how to add a new country adapter using the M0 contracts.

> Disclaimer: No medical advice. Adapters should only ingest public, non-clinical data.

## 1. Create a project

Create a new project in `src/`, for example:
- `BloodWatch.Adapters.Spain`

Reference `BloodWatch.Core`.

## 2. Implement `IDataSourceAdapter`

Each adapter must provide:
- `AdapterKey`
- `GetAvailableRegionsAsync()`
- `FetchLatestAsync()` returning canonical `Snapshot`

Map source-specific fields into canonical models:
- `SourceRef`
- `RegionRef`
- `Metric`
- `SnapshotItem`

## 3. Register adapter in DI

Register in API/Worker startup:

```csharp
builder.Services.AddSingleton<IDataSourceAdapter, YourAdapter>();
```

## 4. Add source seed (optional but recommended)

Add a source row in `BloodWatchDbContext` seed data for discoverability and stable IDs.

## 5. Add tests

Create tests for:
- Payload parsing/mapping
- Region normalization
- Metric normalization
- Empty/null tolerance

## 6. Document data source

Document source URL, update cadence, and known caveats in docs.
