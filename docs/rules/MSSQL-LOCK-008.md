# MSSQL-LOCK-008 — Sch-M locks on multiple tables in one transaction (deadlock potential)

**Default severity:** Warning · **Category:** Locking

## What it checks

One explicit transaction that takes Sch-M locks on **two or more different tables**. The
finding is anchored to the first Sch-M statement and names the tables involved.

## Why it matters

While the migration's transaction holds Sch-M on table A and then asks for Sch-M on table B,
any concurrent session that touches B first and then A is a deadlock waiting to happen. One of
the two gets chosen as the deadlock victim — if it is the migration, the deploy fails halfway;
if it is the application, users see errors. Multi-table lock acquisition inside a single
transaction is exactly the pattern deadlock detectors feed on.

## Example

```sql
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
```

Reports: `Warning MSSQL-LOCK-008 … This transaction takes schema-modification (Sch-M) locks on
2 different tables (dbo.A, dbo.B); sessions locking them in another order can deadlock.`

Two Sch-M statements against the *same* table do not trigger this rule (that is
MSSQL-LOCK-007's concern). "Same table" ignores spelling: `[T]`, `T`, `dbo.T`, `[DBO].[t]` and
the table part of an `sp_rename` literal (`N'[T].[Column]'`) are one table — an unqualified
name is assumed to live in `dbo`, so `audit.T` and `dbo.T` remain two tables.

## How to fix

Modify the tables in separate transactions, and when multiple objects must be locked together,
always acquire them in one consistent, documented order (the same order the application uses).

## Assumptions (version / edition)

Not version or edition dependent.
