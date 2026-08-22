# MSSQL-TRAN-006 — Long explicit transaction

**Default severity:** Info · **Category:** Transaction & script hygiene

## What it checks

One explicit transaction (`BEGIN TRAN … COMMIT`) that wraps **25 or more working statements**:
DDL, DML, DCL, dynamic SQL and procedure calls. Control-flow wrappers (`IF`, `BEGIN…END`, `WHILE`,
`TRY`), `SET`, `DECLARE` and `PRINT` are not counted — 25 guarded inserts (`IF NOT EXISTS (…)
INSERT …`) are 25 working statements, not 50. Anchored to the `BEGIN TRAN`.

MSSQL-LOCK-007 counts the Sch-M locks inside a transaction; this rule looks at its *length*
regardless of lock type.

## Why it matters

Every lock any statement in the transaction takes — the X locks of each `INSERT`/`UPDATE`, the
Sch-M of each `ALTER` — is held **until the final COMMIT**. The blocking window is therefore not
the duration of the slowest statement but the run time of the whole transaction, and every
session that touches any of those rows or tables waits for all of it. A seed script with three
hundred inserts in one transaction blocks readers of the lookup table for the whole three hundred,
and a failure on the 299th rolls back the 298 that were fine.

## Example

```sql
BEGIN TRAN;
INSERT INTO dbo.Lookup (Id, Name) VALUES (1, 'Value 1');
INSERT INTO dbo.Lookup (Id, Name) VALUES (2, 'Value 2');
-- … 28 more …
COMMIT;
```

Reports on line 1: `Info MSSQL-TRAN-006 This transaction wraps 30 statements; every lock any of
them takes is held until the final COMMIT, so the blocking window is the run time of the whole
transaction.`

Quiet: the same 30 inserts split into three transactions of 10, or run in autocommit with no
explicit transaction at all, or 13 guarded inserts (13 working statements) in one transaction.

## How to fix

Split independent steps into separate transactions — one per table, or one per batch of rows —
so locks are released as each step commits:

```sql
BEGIN TRAN;
INSERT INTO dbo.Lookup (Id, Name) VALUES (1, 'Value 1');
-- … 9 more …
COMMIT;
BEGIN TRAN;
INSERT INTO dbo.Lookup (Id, Name) VALUES (11, 'Value 11');
-- …
COMMIT;
```

If the steps truly must be atomic, keep the transaction but make the statements inside it as
cheap as possible (set-based inserts instead of one row per statement) and schedule a quiet
window.

## Assumptions (version / edition)

Not version or edition dependent. The threshold (25) is fixed; the rule is Info because a long
transaction is sometimes exactly what is wanted — it is a prompt to decide, not a verdict.
