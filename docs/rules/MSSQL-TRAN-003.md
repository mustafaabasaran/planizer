# MSSQL-TRAN-003 — Transaction spans GO batches

**Default severity:** Warning · **Category:** Transaction & script hygiene

## What it checks

A `BEGIN TRAN` in one `GO` batch whose matching `COMMIT` (or `ROLLBACK`) is in a later batch of
the same file. The pairing comes from the same main-path walk as MSSQL-TRAN-002; the finding is
anchored to the `BEGIN TRAN` and says how many batches later it is closed.

## Why it matters

A transaction does survive `GO` — `GO` is a client-side separator, and `@@TRANCOUNT` is a
property of the session. What does **not** survive is error handling: when a batch in between
fails, the client tool moves on to the next batch (sqlcmd does by default; so do most runners),
the `COMMIT` batch may never be reached or may commit a half-applied change, and if the script
stops early the transaction is left open with all its locks. `SET XACT_ABORT ON` does not close
this gap either — it aborts the *batch*, and the following batches still run inside the
transaction it left open. The most common source in the corpus scan was the EF Core idempotent
script shape, `BEGIN TRANSACTION; GO … COMMIT; GO`, with a `GO` after every step.

## Example

```sql
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
GO
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
```

Reports on line 1: `Warning MSSQL-TRAN-003 The transaction opened at line 1 is committed at
line 5, 1 GO batch later; if a batch in between fails, the transaction stays open and the
remaining batches run inside it.`

Quiet: one transaction per batch.

```sql
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
GO
BEGIN TRAN;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
GO
```


Up to five GO-spanning transactions in a file are reported one by one. Above that the file gets a
**single** finding ([ADR-0001](../adr/0001-rev-002-dml-findings-aggregated-per-file.md) shape)
anchored at the first `BEGIN TRAN`, with the count and the first examples — EF Core idempotent
scripts wrap every migration in `BEGIN TRANSACTION; GO … COMMIT; GO` and would otherwise produce
hundreds of identical warnings per file. Transactions whose `BEGIN TRAN` carries
`-- planizer:ignore MSSQL-TRAN-003` leave the count.

## How to fix

Keep `BEGIN TRAN` and its `COMMIT` in the same batch — remove the `GO`s between them when nothing
in between needs its own batch, or commit before each `GO` and open a new transaction after it.
When a step genuinely needs a batch boundary inside the transaction (a `CREATE PROCEDURE` that
must be alone in its batch), run that step through `EXEC sp_executesql N'…'` instead of `GO`.

## Assumptions (version / edition)

Not version or edition dependent. Batches are tracked per file.
