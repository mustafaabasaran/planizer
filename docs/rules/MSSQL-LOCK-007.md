# MSSQL-LOCK-007 — Multiple Sch-M locks held until COMMIT in one transaction

**Default severity:** Critical · **Category:** Locking

## What it checks

Two or more Sch-M-acquiring statements inside one explicit `BEGIN TRAN … COMMIT`. The finding
is anchored to the **first** Sch-M statement of the transaction and reports how many there are.

## Why it matters

A Sch-M lock taken inside an explicit transaction is not released when the statement finishes —
it is held **until COMMIT**. With several Sch-M statements in one transaction, the blocking
windows do not run one after another and release; they accumulate: by the end, every touched
table is locked simultaneously, and the total blocking window is the *sum* of all the
statements. What looks like "three quick ALTERs" becomes one long multi-table outage.

## Example

```sql
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
```

Reports (on the first ALTER): `Critical MSSQL-LOCK-007 … This transaction takes
schema-modification (Sch-M) locks in 2 statements and holds every one of them until COMMIT; the
total blocking window is the sum of all of them.`

## How to fix

Split the transaction so each Sch-M statement commits on its own:

```sql
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
BEGIN TRAN;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
```

If the statements genuinely must be atomic, keep the transaction as short as possible, add
`SET LOCK_TIMEOUT` (MSSQL-LOCK-010), and schedule a maintenance window.

## Assumptions (version / edition)

Not version or edition dependent. Which statements count as Sch-M-acquiring comes from the
behavior catalog (ALTER TABLE, DROP/TRUNCATE TABLE, sp_rename, offline index operations, …).
