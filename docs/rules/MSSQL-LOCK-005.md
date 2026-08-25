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

## Limitations of the fix

`RESUMABLE = ON` cannot simply be pasted onto every online index operation. Microsoft's
"Resumable index operations / Current functional limitations" states it plainly:

> The DDL command with RESUMABLE = ON can't be executed inside an explicit transaction.

Adding the option inside a `BEGIN TRANSACTION … COMMIT` block turns a working migration into one
that fails at runtime with **error 574**. Planizer therefore looks at the transaction context:

```sql
BEGIN TRANSACTION;
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
COMMIT;
```

still reports the Info — a killed build loses its progress here exactly as it does anywhere else —
but the suggested fix becomes *"move the `CREATE INDEX` out of the `BEGIN TRANSACTION … COMMIT`
block, then add `RESUMABLE = ON`"* instead of *"add `RESUMABLE = ON`"*.

The script is not the whole story: a migration runner can open a transaction the script never
mentions (DbUp with a transaction per script, SSDT, EF's transactional scripts). That transaction
is invisible offline and breaks `RESUMABLE = ON` the same way, so **every** MSSQL-LOCK-005 fix
carries the standing caveat that the index operation has to run outside any transaction — the
runner's included.

## Assumptions (version / edition)

Version gated: `REBUILD` is resumable from `--target-version` 2017, `CREATE INDEX` from 2019.
Edition gated to Enterprise/Azure — on Standard/Express the `ONLINE = ON` statement fails
outright (see MSSQL-LOCK-003), so this rule stays silent there.
