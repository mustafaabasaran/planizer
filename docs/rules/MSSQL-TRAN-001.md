# MSSQL-TRAN-001 — Explicit transaction without SET XACT_ABORT ON

**Default severity:** Warning · one finding per file · **Category:** Transaction & script hygiene

## What it checks

The file opens an explicit transaction (`BEGIN TRAN` / `BEGIN TRANSACTION`) at a point where no
`SET XACT_ABORT ON` is in effect. The option is tracked in script order across `GO` — it belongs
to the session, so an `ON` in an earlier batch still covers a later transaction — and the last
setting wins: `SET XACT_ABORT ON; SET XACT_ABORT OFF; BEGIN TRAN;` is reported. One finding per
file, anchored to the first `BEGIN TRAN`.

## Why it matters

With `XACT_ABORT OFF` — the default for most client libraries and for sqlcmd — many run-time
errors are *statement-terminating*, not *batch-terminating*: a lock timeout (1222), a constraint
violation (547), a conversion error (245) abort only the statement that failed. The batch carries
on, the `COMMIT` at the end commits whatever did succeed, or — if the batch ends without reaching
it — the transaction is simply **left open, holding every lock it took, until the connection is
closed**. Behind a migration runner that keeps its connection pooled, that can be a long time.
`SET XACT_ABORT ON` turns every such error into "roll back the transaction and abort the batch",
which is the behaviour a migration wants.

## Example

```sql
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
```

Reports on line 1: `Warning MSSQL-TRAN-001 This script opens an explicit transaction (line 1)
with XACT_ABORT OFF; a run-time error such as a lock timeout or constraint violation then aborts
only the failing statement, and the transaction stays open — holding its locks — until the
connection closes.`

Quiet — including across a batch boundary:

```sql
SET XACT_ABORT ON;
GO
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
```

## How to fix

Put the option at the top of the script:

```sql
SET XACT_ABORT ON;
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
```

It combines well with `TRY … CATCH` (MSSQL-TRAN-004, MSSQL-TRAN-005): `XACT_ABORT` makes the
transaction uncommittable on error, the `CATCH` rolls it back and rethrows.

## Assumptions (version / edition)

Not version or edition dependent. Files with no explicit transaction are never reported — in
autocommit mode each statement is its own transaction and nothing can be left open.
