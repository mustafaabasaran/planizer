# MSSQL-LOCK-002 — Offline index build blocks access to the table

**Default severity:** Warning · **Category:** Locking

## What it checks

`CREATE INDEX` without `ONLINE = ON`:

- a **nonclustered** build holds a shared (S) table lock — writes are blocked during the build;
- a **clustered** build holds a Sch-M lock — *all* access is blocked.

## Why it matters

An index build takes as long as the table is big. Offline, that whole time is a blocking
window: on a nonclustered build every `INSERT`/`UPDATE`/`DELETE` waits; on a clustered build
even reads wait. On a large production table this is the classic "the deploy froze the app"
migration.

## Example

```sql
CREATE INDEX IX_T_C ON dbo.T (C);
CREATE CLUSTERED INDEX CX_T ON dbo.T (C);
```

Reports (Standard edition, the default assumption):

- `Warning MSSQL-LOCK-002 … Offline nonclustered index build takes a shared (S) lock on dbo.T:
  writes blocked during build.`
- `Warning MSSQL-LOCK-002 … Offline clustered index build takes a schema-modification (Sch-M)
  lock on dbo.T: all access blocked until the build completes.`

## How to fix

The suggested fix depends on the edition assumption:

- **Enterprise / Azure:** build the index online — `CREATE INDEX … WITH (ONLINE = ON);`
  (also see MSSQL-LOCK-004 and MSSQL-LOCK-005 for `WAIT_AT_LOW_PRIORITY` and `RESUMABLE`).
- **Standard / Express:** `ONLINE` is not available (see MSSQL-LOCK-003); schedule a
  maintenance window instead.

## Assumptions (version / edition)

The finding itself fires on every edition; only the fix text changes. Online index build is an
Enterprise (and Azure) feature.
