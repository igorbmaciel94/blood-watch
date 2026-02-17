# Portugal Reservas Data Source

Official public source for blood reserves in Portugal (SNS Transparencia):

- Dataset page: https://transparencia.sns.gov.pt/explore/dataset/reservas/information/
- Download endpoint used by adapter: https://transparencia.sns.gov.pt/explore/dataset/reservas/download?format=json&timezone=UTC&use_labels_for_header=false
- API records endpoint (also supported by mapper shape tolerance): https://transparencia.sns.gov.pt/api/explore/v2.1/catalog/datasets/reservas/records

## Expected fields

BloodWatch maps the following source fields:

- `periodo`: reference month (for example `2015-12`)
- `regiao`: health region display name
- `entidade`: institution/hospital (fallback label when `regiao` is missing)
- `grupo_sanguineo`: blood-group dimension (for example `Total`, `A+`)
- `reservas`: numeric reserves value (number or string, null tolerated)

Optional/unmapped fields are ignored (for example `localizacao_geografica`, `geometry`, `record_timestamp`, unknown extra keys).

## Canonical mapping

- `Snapshot.Source`: `pt-transparencia-sns` / `Portugal SNS Transparency`
- `Snapshot.ReferenceDate`: parsed from `periodo` (normalized to first day of month)
- `SnapshotItem.Region`:
  - key: normalized stable key (for example `pt-norte`, `pt-centro`, `pt-lvt`, `pt-alentejo`, `pt-algarve`)
  - display: original `regiao` text
- `SnapshotItem.Metric`:
  - key: `overall` when no breakdown exists (`Total`/missing only)
  - key: `blood-group-<normalized>` when group breakdown exists
- `SnapshotItem.Unit`: `units`
- `SnapshotItem.Value`: parsed decimal `reservas` value
