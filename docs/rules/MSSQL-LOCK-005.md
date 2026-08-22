# MSSQL-LOCK-005 — Online index operation without RESUMABLE

**Default severity:** Info · **Category:** Locking

## What it checks

Online index operations that could be resumable but are not:

- `ALTER INDEX … REBUILD WITH (ONLINE = ON)` without `RESUMABLE = ON`, on 2017+;
- `CREATE INDEX … WITH (ONLINE = ON)` without `RESUMABLE = ON`, on 2019+.

## Why it matters

A long online index operation that fails — or has to be killed because it is in the way — loses
**all** of its progress, and a long rollback can block the table while it unwinds. A resumable
operation can be paused (`ALTER INDEX … PAUSE`), survives failovers, and continues where it
left off. For an index that takes hours to build on production data, this is the difference
between "resume at 90%" and "start over tonight".

## Example

```sql
-- analyzed with --target-version 2019 --edition enterprise
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
```

Reports: `Info MSSQL-LOCK-005 … Online CREATE INDEX on dbo.T is not resumable; a failure or
abort loses all progress and any long rollback blocks the table.`

## How to fix

```sql
CREATE INDEX IX ON dbo.T (C)
WITH (ONLINE = ON, RESUMABLE = ON, MAX_DURATION = 60 MINUTES);
```

`MAX_DURATION` auto-pauses the operation after the given time instead of letting it run
unbounded.

## Assumptions (version / edition)

Version gated: `REBUILD` is resumable from `--target-version` 2017, `CREATE INDEX` from 2019.
Edition gated to Enterprise/Azure — on Standard/Express the `ONLINE = ON` statement fails
outright (see MSSQL-LOCK-003), so this rule stays silent there.
