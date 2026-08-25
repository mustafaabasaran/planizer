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

`ONLINE = ON` does not mean "no locks". An online index operation runs in three phases, and the
first and last of them each need a short table lock:

| Operation | Preparation (start) | Final (completion) |
|---|---|---|
| Online `CREATE INDEX` (nonclustered) | shared (S) | shared (S) |
| Online `CREATE`/`DROP` of a **clustered** index | shared (S) | schema-modification (Sch-M) |
| Online `ALTER INDEX … REBUILD` (clustered or not) | shared (S) | schema-modification (Sch-M) |

So an online nonclustered build never takes a blocking table Sch-M at all, while a clustered
create/drop and every rebuild finish on one. Either way the brief lock queues at *normal*
priority — so if one long-running transaction holds a conflicting lock, the index operation
waits, and **every request behind it waits too** (a lock convoy). One slow report can turn a
"safe online rebuild" into a site-wide stall. `WAIT_AT_LOW_PRIORITY` (2014+) makes that brief
wait polite and defines what happens if it cannot get in.

Independently of the phase locks, every online index operation also holds a Sch-M **object** lock
of resource subtype `INDEX_OPERATION` for its whole duration; per Microsoft's own footnote that
lock blocks concurrent DDL on the object, not DML — which is why Planizer must not let it feed a
"blocks all access" count (`Sch-M` in the script summary). An online nonclustered create is
therefore reported here but is *not* counted as a Sch-M taker.

## Example

```sql
-- analyzed with --edition enterprise (default target 2019)
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);
```

Reports: `Info MSSQL-LOCK-004 … Online ALTER INDEX REBUILD still needs a brief shared (S) lock on
dbo.T to start and a schema-modification (Sch-M) lock to complete; without WAIT_AT_LOW_PRIORITY
they queue at normal priority and can convoy blocked sessions.`

A nonclustered `CREATE INDEX … WITH (ONLINE = ON)` reports the shared-only variant
(`… a brief shared (S) lock on dbo.T to start and again to complete …`), and only with
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

One deliberate consequence of these semantics: an online **nonclustered** `CREATE INDEX` no
longer counts toward the report's "N taking Sch-M locks" summary, nor toward
[MSSQL-LOCK-007](MSSQL-LOCK-007.md)/[MSSQL-LOCK-008](MSSQL-LOCK-008.md)'s
Sch-M-in-transaction analysis — its table locks are brief S locks, and the duration-held
`INDEX_OPERATION` Sch-M blocks concurrent DDL, not DML.

## Sources

- *How online index operations work* (SQL Server docs) — the three-phase table quoted above:
  "At the end of the operation, for a short period of time, a shared (S) lock is acquired on the
  object if a nonclustered index is being created. A schema modification (Sch-M) lock is acquired
  when a clustered index is created or dropped online and when a clustered or nonclustered index
  is being rebuilt."
- *Guidelines for online index operations* — "Because a shared (S) lock or a schema modification
  (Sch-M) lock is held in the final phase of the index operation…"; i.e. the Sch-M is a
  **final-phase** lock, not a start-and-end one.
- `CREATE INDEX` / `ALTER INDEX` `ONLINE = ON` bullet — the long-held Sch-M is an object lock of
  resource subtype `INDEX_OPERATION` that prevents concurrent DDL, not DML.
