# MSSQL-SET-002 — Many DML statements without SET NOCOUNT ON

**Default severity:** Info · one finding per file · **Category:** Transaction & script hygiene

## What it checks

A file with **50 or more** data-modification statements (`INSERT`, `UPDATE`, `DELETE`, `MERGE`,
`BULK INSERT` — plain `SELECT`s do not count, temp-table targets do) and no `SET NOCOUNT ON`
anywhere in it. Statements nested in `IF` / `WHILE` / `TRY`
bodies count; module bodies do not. One Info per file (the ADR-0001 pattern), anchored to the
first DML statement; statements carrying `-- planizer:ignore MSSQL-SET-002` leave the count.

## Why it matters

Without `NOCOUNT`, SQL Server sends a `DONE` token — the "(1 row affected)" message — back to
the client after every statement. For a seed script with two thousand single-row inserts that is
two thousand extra messages in the response stream (not extra round trips — the tokens ride the
same TDS response — but bytes, parsing and info events on the client), and two thousand lines in
the migration runner's log burying the one line that matters when something goes wrong. It is
cheap to fix and the reason every stored-procedure template starts with it.

## Example

```sql
INSERT INTO dbo.Seed (Id, Name) VALUES (1, N'row 1');
INSERT INTO dbo.Seed (Id, Name) VALUES (2, N'row 2');
-- … 48 more …
```

Reports once, on line 1: `Info MSSQL-SET-002 50 data-modification statements in this file run
without SET NOCOUNT ON: each one returns a "rows affected" message to the client, which adds a
DONE message per statement to the response and a line to the migration runner's log.`

49 statements stay under the threshold; the same 50 with `SET NOCOUNT ON;` on top are quiet.

## How to fix

```sql
SET NOCOUNT ON;
INSERT INTO dbo.Seed (Id, Name) VALUES (1, N'row 1');
-- …
```

If the runner relies on the row counts (some deployment tools log them on purpose), disable the
rule for that directory in its `.planizer.json`:

```json
{ "rules": { "MSSQL-SET-002": { "enabled": false } } }
```

## Assumptions (version / edition)

Not version or edition dependent. The threshold (50) is fixed.
