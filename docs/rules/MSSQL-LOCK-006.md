# MSSQL-LOCK-006 — Offline index rebuild blocks all access to the table

**Default severity:** Warning · **Category:** Locking

## What it checks

`ALTER INDEX … REBUILD` without `ONLINE = ON`.

## Why it matters

An offline rebuild holds a schema-modification (Sch-M) lock on the table for the entire
rebuild — all reads and writes are blocked for as long as the rebuild takes. On a big index
that is a long, complete outage of the table, often scheduled innocently as "index
maintenance".

## Example

```sql
ALTER INDEX IX ON dbo.T REBUILD;
```

Reports: `Warning MSSQL-LOCK-006 … Offline ALTER INDEX REBUILD takes a schema-modification
(Sch-M) lock on dbo.T for the duration of the rebuild; all reads and writes are blocked.`

`ALTER INDEX ALL ON dbo.T REBUILD;` triggers the same finding.

## How to fix

- **Enterprise / Azure:** rebuild online — `ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);`
  (see also MSSQL-LOCK-004/005 for `WAIT_AT_LOW_PRIORITY` and `RESUMABLE`).
- **Every edition:** `ALTER INDEX IX ON dbo.T REORGANIZE;` is *always* online. It is slower and
  only compacts (it does not fully rebuild or update statistics), but it never blocks — for
  moderate fragmentation it is usually the better default.

## Assumptions (version / edition)

Fires on every edition; the fix text depends on `--edition` (Enterprise/Azure get the
`ONLINE = ON` suggestion, Standard/Express get the REORGANIZE alternative only).
