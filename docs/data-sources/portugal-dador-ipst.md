# Portugal Dador/IPST Data Source

Official public source for blood reserve status and donation logistics in Portugal:

- Base API: `https://dador.pt/api`
- Blood reserves endpoint: `https://dador.pt/api/blood-reserves`
- Institutions endpoint: `https://dador.pt/api/institutions`
- Sessions endpoint: `https://dador.pt/api/sessions`

## Canonical source identity

- `source`: `pt-dador-ipst`
- `name`: `Portugal Dador/IPST`

## Reserve mapping

`/api/blood-reserves` is mapped to canonical reserve status items.

- Metrics: blood groups (`A+`, `A-`, `B+`, `B-`, `AB+`, `AB-`, `O+`, `O-`)
- Regions: `IPST`, `NACIONAL`, and regional blocks (`NORTE`, `CENTRO`, `LISBOA E SETUBAL`, `ALENTEJO`, `ALGARVE`)
- Status color mapping:
  - `VERMELHO -> critical`
  - `LARANJA -> warning`
  - `AMARELO -> watch`
  - `VERDE -> normal`
  - unknown values -> `unknown`

Reserves are status-only for v1 (no numeric reserve value/unit).

## Freshness / staleness

- Worker stores and exposes the source version timestamp from dador payload (`data.version`) as reference metadata.
- Alert evaluation runs every worker cycle and is not blocked by source staleness.

## Geo & session mapping

- `/api/institutions` populates `donation_centers` with region, district/municipality, and coordinates.
- `/api/sessions` populates `collection_sessions` with date, location, type, and institution linkage.
