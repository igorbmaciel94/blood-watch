# ADR 0002 — Postgres as primary datastore from day one

## Status
Accepted

## Context
We need:
- migrations
- consistent query semantics across dev and prod
- historical snapshots + indexing

Switching from SQLite later can introduce subtle differences and migration churn.

## Decision
Use Postgres as primary datastore for dev and prod. Use Docker Compose for local Postgres.

## Consequences
+ Consistent behavior across environments
+ Better indexing and querying
- Slightly heavier local setup (mitigated by Docker)
