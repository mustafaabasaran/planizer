# MSSQL-REV-001 — Statement is irreversible; data cannot be restored

**Default severity:** Critical · **Category:** Reversibility

## What it checks

Statements that destroy data no rollback script can bring back:

- `DROP TABLE`,
- `ALTER TABLE … DROP COLUMN`,
- `TRUNCATE TABLE`,
- `DELETE` without a `WHERE` clause.

The DDL cases are resolved through the behavior catalog (`reversible=no`); the unbounded DELETE
is flagged directly. ALTER COLUMN narrowing is also irreversible but cannot be proven from the
script alone offline — the RW family (MSSQL-RW-005/006) carries that risk instead.

## Why it matters

Every other problem in a migration can be retried, waited out, or rolled back. Destroyed data
cannot. Once the transaction commits, the only way back is a restore — with downtime and data
loss for everything written since. A migration review must treat these statements differently
from everything else: they are the ones you cannot take back.

## Example

```sql
DROP TABLE dbo.LegacyOrders;
ALTER TABLE dbo.Customers DROP COLUMN TaxCode;
TRUNCATE TABLE dbo.AuditLog;
DELETE FROM dbo.SessionCache;
```

All four report Critical. For the DROP TABLE the suggested fix is generated as SQL:

```sql
EXEC sp_rename 'dbo.LegacyOrders', 'LegacyOrders_deprecated';
```

For the TRUNCATE and the WHERE-less DELETE the suggested fix is a keep-a-copy-first snippet with
a date-stamped, data-only target (see *How to fix* below for why both of those matter):

```sql
SELECT * INTO dbo.AuditLog_backup_<yyyymmdd> FROM dbo.AuditLog;
```

A `DELETE … WHERE`, an `ADD COLUMN`, or a `CREATE INDEX` does not trigger the rule.


A WHERE-less DELETE on a table variable or temp table is not irreversible (no persistent data),
and a DELETE bounded by a JOIN in its FROM clause is not a full-table wipe; neither is flagged.


DDL on temp tables (`DROP TABLE #t`, `TRUNCATE TABLE #t`) is ignored as well — nothing persistent
is lost.

## How to fix

Use the **expand/contract** pattern: instead of dropping now, rename to `<name>_deprecated`,
release, watch for anything that breaks, and drop in a *later* release. For unbounded DELETE
and TRUNCATE, keep a copy first:

```sql
-- Data-only copy: SELECT … INTO transfers no indexes, constraints or triggers,
-- which is what a restore path needs.
-- Date-stamp the target; a fixed _backup name already exists on a re-run
-- and fails with error 2714.
SELECT * INTO dbo.SessionCache_backup_<yyyymmdd> FROM dbo.SessionCache;
-- Verify the copy, then delete in batches with a WHERE clause.
```

Two details in that snippet are deliberate.

**The date stamp.** A fixed `_backup` suffix is a landmine in a script that may be re-run: the
target already exists the second time around and `SELECT … INTO` fails outright —

> There is already an object named 'SessionCache_backup' in the database. (error 2714)

Replace `<yyyymmdd>` with the deployment date (or any other unique token) so the second run
creates its own copy instead of dying on the first statement. A tool that ships three idempotency
rules should not hand out a fix that breaks on a re-run.

**The copy holds data only.** `SELECT … INTO` is not a table clone:

> Indexes, constraints, and triggers defined in the source table aren't transferred to the new
> table, nor can you specify them in the SELECT...INTO statement.

That is the right shape here and does not need fixing. The copy exists to answer "what was in
those rows", and the indexes, constraints and triggers are still on the *source* table — the
DELETE/TRUNCATE removes rows, not the table's definition. Recreating them on the backup would
only slow the copy down and duplicate constraint names.

## Assumptions (version / edition)

Not version or edition dependent. Which DDL counts as irreversible comes from the behavior
catalog (`reversible=no` rows: `drop_table`, `drop_column`, `truncate_table`).
