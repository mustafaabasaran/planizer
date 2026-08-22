# MSSQL-RW-012 — Changing DATA_COMPRESSION rewrites the whole table or index

**Default severity:** Critical (Blocker when compression is unavailable) · **Category:** Rewrite vs metadata-only

## What it checks

A `DATA_COMPRESSION` option on `ALTER TABLE … REBUILD` or `ALTER INDEX … REBUILD`. The
edition/version matrix comes from the behavior catalog:

- compression available (Enterprise any version; Standard/Express from **2016 SP1**) →
  **Critical**: full rewrite under Sch-M,
- compression unavailable (Standard/Express **before 2016 SP1**) → **Blocker**: the statement
  simply fails (error 7738).

## Why it matters

Enabling ROW or PAGE compression is not a flag flip — it is a complete physical rewrite of the
table or index, with all the locking of an offline rebuild (Sch-M for the duration). Teams
reach for compression precisely on their *largest* tables, which is where the rewrite hurts
most. And the edition trap has two layers: before 2016 SP1 compression was Enterprise-only, so
the same migration that worked on the Developer box fails outright on an old Standard server.

## Example

```sql
ALTER TABLE dbo.BigTable REBUILD WITH (DATA_COMPRESSION = PAGE);
ALTER INDEX IX_BigTable_Code ON dbo.BigTable REBUILD WITH (DATA_COMPRESSION = ROW);
```

With `--target-version 2019 --edition standard`, both report Critical:
`Changing DATA_COMPRESSION fully rewrites … while holding a Sch-M lock.`
With `--target-version 2014 --edition standard`:
`Blocker MSSQL-RW-012 … DATA_COMPRESSION is not available on this edition before SQL Server
2016 SP1; rebuilding table dbo.BigTable with compression will fail.`

A plain `REBUILD` without the option is judged by MSSQL-LOCK-006 instead.

## How to fix

For the Critical case: combine with `ONLINE = ON` (Enterprise) or run in a maintenance window,
and compress the largest partitions/indexes one at a time rather than the whole table at once.
For the Blocker case: remove the DATA_COMPRESSION option — or correct the
`--target-version`/`--edition` assumption if the server actually supports compression.

## Assumptions (version / edition)

Strongly both. Catalog rows: `data_compression_change` for Enterprise (any version) and for
standard_express from `2016sp1`; no applicable row means unavailable → Blocker.
