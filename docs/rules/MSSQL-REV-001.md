# MSSQL-REV-001 — Statement is irreversible; data cannot be restored

**Default severity:** Critical · **Category:** Reversibility

## What it checks

Statements that destroy data no rollback script can bring back:

- `DROP TABLE`,
- `ALTER TABLE … DROP COLUMN`,
- `TRUNCATE TABLE`,
- `DELETE` that nothing bounds — no `WHERE`, no `TOP`, and no join that can drop target rows.

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

## When a JOIN does not save you

A `JOIN` in the FROM clause is not a filter by itself; it counts only when it can actually **drop
rows of the target**. The rule shares
[MSSQL-LOCK-009](MSSQL-LOCK-009.md)'s three-state classification and acts on two of the states:

| Shape | State | Reported |
|---|---|---|
| Target on the **null-supplying** side of a `LEFT`/`RIGHT` outer join | bounded | nothing |
| Target on the **preserved** side of a `LEFT`/`RIGHT` outer join, either side of `FULL OUTER JOIN`, any `CROSS JOIN` or comma cross join, `OUTER APPLY` with the target on the left | unbounded | **Critical** |
| `INNER JOIN`, `CROSS APPLY` | **inconclusive** | nothing here; MSSQL-LOCK-009 reports Info |

```sql
DELETE t FROM dbo.Orders t LEFT JOIN dbo.Customers u ON u.Id = t.CustomerId;
```

This deletes **every** row of `dbo.Orders` — an outer join preserves its left side in full — and
reports `Critical MSSQL-REV-001 The LEFT JOIN does not restrict dbo.Orders; every row is deleted
and cannot be restored — keep a copy first and delete in batches.`

The inconclusive state is deliberately *not* escalated here: whether an `INNER JOIN` or a
`CROSS APPLY` restricts the target depends on the data, and a Critical data-loss finding on a
guess would be wrong. MSSQL-LOCK-009 carries that uncertainty as Info + `inconclusive: true`
instead, and a schema/statistics snapshot (Phase 2) settles it.

A WHERE-less DELETE on a table variable or temp table is not irreversible either (no persistent
data), whether the target is named directly or through an alias.
DDL on temp tables (`DROP TABLE #t`, `TRUNCATE TABLE #t`) is ignored as well — nothing persistent
is lost.

## How to fix

Use the **expand/contract** pattern: instead of dropping now, rename to `<name>_deprecated`,
release, watch for anything that breaks, and drop in a *later* release. For an unbounded DELETE
and for TRUNCATE, keep a copy first:

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
