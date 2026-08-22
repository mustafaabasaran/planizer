# MSSQL-ENV-003 — Long-running DDL with no progress messages

**Default severity:** Info · one finding per file · **Category:** Transaction & script hygiene

## What it checks

The file contains DDL that, under the configured target version and edition, **rewrites a table,
scans it in full or builds an index** — the behavior catalog's `rewrite`, `full_scan` and
`index_build` classes: `CREATE INDEX`, `CREATE CLUSTERED INDEX`, `ALTER INDEX … REBUILD`, `ADD
CONSTRAINT CHECK / FOREIGN KEY` (with check), `ALTER COLUMN … NOT NULL`, `ADD … NOT NULL DEFAULT
…` on Standard/Express, … — and not a single progress message: no `PRINT`, no `RAISERROR` with
severity 0–10, no `RAISERROR … WITH NOWAIT`. A `RAISERROR` with severity 11 or higher is error
handling, not progress, and does not count. One Info per file (the ADR-0001 pattern), anchored
to the first long-running statement, listing the first three. Statements the catalog cannot
classify offline are not counted; statements carrying `-- planizer:ignore MSSQL-ENV-003` leave
the count.

## Why it matters

A clustered index build on a large table can take twenty minutes. For those twenty minutes the
runner log shows the last line it printed — the previous script's name — and nothing else. Is it
the index? The constraint check? Is it blocked behind somebody's open transaction? Without a
message per step there is no way to tell from the outside, and the person watching the deployment
has to choose between waiting blind and killing a migration that was about to finish. A
`RAISERROR(…, 0, 1) WITH NOWAIT` before each long step costs nothing and is flushed to the client
immediately (plain `PRINT` is buffered until the batch ends, which is why `WITH NOWAIT` is the
better form).

## Example

```sql
CREATE INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);
CREATE CLUSTERED INDEX CX_Orders ON dbo.Orders (Id);
```

Reports once, on line 1: `Info MSSQL-ENV-003 2 statements in this file rewrite, scan or build an
index over a whole table (\`CREATE INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);\`,
\`CREATE CLUSTERED INDEX CX_Orders ON dbo.Orders (Id);\`) and the script prints no progress
message: a long run is silent until it finishes or fails, with no way to tell which step is
taking the time.`

Edition matters: `ALTER TABLE dbo.Orders ADD IsArchived bit NOT NULL CONSTRAINT DF_Orders_IsArchived
DEFAULT 0;` is reported on Standard (the table is rewritten) and quiet on Enterprise (metadata
only). A file with only metadata-only DDL (`ADD … NULL`, `DROP COLUMN`, `sp_rename`) is never
reported, and neither is a file that has a `RAISERROR('index build failed', 16, 1)` in its `CATCH`
but no progress message on the main path.

## How to fix

Announce each step so the runner log shows where the script is:

```sql
RAISERROR('step 1: index Orders by customer', 0, 1) WITH NOWAIT;
CREATE INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);
RAISERROR('step 2: cluster Orders', 0, 1) WITH NOWAIT;
CREATE CLUSTERED INDEX CX_Orders ON dbo.Orders (Id);
```

## Assumptions (version / edition)

Which statements count as long-running comes from the behavior catalog under the configured
`--target-version` and `--edition`, so the finding can appear or disappear with the edition (see
the example). The rule has no idea how large the table is — offline every rewrite is potentially
long; the snapshot/live modes of Phase 2 will let it weigh the table size.
