# MSSQL-LOCK-010 — Transactional DDL without SET LOCK_TIMEOUT

**Default severity:** Warning · **Category:** Locking

## What it checks

An explicit transaction that contains DDL, with **no `SET LOCK_TIMEOUT`** appearing earlier in
the same script. One finding per transaction, anchored to its first DDL statement.

## Why it matters

The default lock timeout is **infinite**. If the Sch-M lock a DDL statement needs is contended
— one long-running report is enough — the migration waits forever, and because Sch-M requests
queue ahead of ordinary lock requests, *everything else* on the table queues behind the waiting
migration. The result is an outage caused not by the DDL running, but by the DDL *waiting*.
A lock timeout turns that scenario into a fast, retryable failure.

## Example

```sql
BEGIN TRAN;
ALTER TABLE dbo.T ADD C int NULL;
COMMIT;
```

Reports: `Warning MSSQL-LOCK-010 … DDL inside an explicit transaction waits indefinitely for
its locks; no SET LOCK_TIMEOUT appears earlier in the script.`

With the timeout set first, the rule stays quiet:

```sql
SET LOCK_TIMEOUT 30000;
BEGIN TRAN;
ALTER TABLE dbo.T ADD C int NULL;
COMMIT;
```

A transaction without DDL (plain DML) is not this rule's concern.

## How to fix

Add at the top of the script:

```sql
SET LOCK_TIMEOUT 30000;
```

A blocked migration then fails after 30 seconds (error 1222) instead of queueing the whole
workload behind it — rerun it when the blocker is gone. Combine with
`WAIT_AT_LOW_PRIORITY` for online index operations (MSSQL-LOCK-004).

## Assumptions (version / edition)

Not version or edition dependent. `SET LOCK_TIMEOUT` is connection-scoped, which is why the
rule accepts any occurrence earlier in the same script file.
