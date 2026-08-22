# MSSQL-LOCK-004 — Online index operation without WAIT_AT_LOW_PRIORITY

**Default severity:** Info · **Category:** Locking

## What it checks

Index operations that run with `ONLINE = ON` but without `WAIT_AT_LOW_PRIORITY`, gated by what
actually accepts the option on the target version:

- `ALTER INDEX … REBUILD` — from SQL Server **2014**;
- `CREATE INDEX` — only from SQL Server **2022** (suggesting it to an older target would
  recommend syntax the server rejects).

Silent on Standard/Express, where `ONLINE = ON` itself cannot run — MSSQL-LOCK-003 already
blocks the statement, and tuning advice would contradict that Blocker.

## Why it matters

`ONLINE = ON` does not mean "no locks": the operation still takes a **brief Sch-M lock** at the
start and at the end. That brief lock queues at *normal* priority — so if one long-running
transaction holds a conflicting lock, the index operation waits, and **every request behind it
waits too** (a lock convoy). One slow report can turn a "safe online rebuild" into a site-wide
stall. `WAIT_AT_LOW_PRIORITY` (2014+) makes the brief Sch-M wait politely and defines what
happens if it cannot get in.

## Example

```sql
-- analyzed with --edition enterprise (default target 2019)
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);
```

Reports: `Info MSSQL-LOCK-004 … Online ALTER INDEX REBUILD still takes brief Sch-M locks on
dbo.T at the start and end; without WAIT_AT_LOW_PRIORITY they queue at normal priority and can
convoy blocked sessions.` A `CREATE INDEX … WITH (ONLINE = ON)` reports the same only with
`--target-version 2022` or later.

## How to fix

```sql
ALTER INDEX IX ON dbo.T REBUILD
WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)));
```

`ABORT_AFTER_WAIT = SELF` cancels the index operation (not the blockers) if it cannot acquire
the lock within `MAX_DURATION` — the migration fails fast instead of convoying production.

## Assumptions (version / edition)

Version gated per statement: `ALTER INDEX … REBUILD` from `--target-version` 2014,
`CREATE INDEX` from 2022 (that is when each starts accepting `WAIT_AT_LOW_PRIORITY`).
Edition gated to Enterprise/Azure — on Standard/Express the `ONLINE = ON` statement fails
outright (see MSSQL-LOCK-003), so this rule stays silent there.
