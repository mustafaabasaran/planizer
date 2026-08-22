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
SELECT * INTO dbo.SessionCache_backup FROM dbo.SessionCache;
-- verify, then delete in batches with a WHERE clause
```

## Assumptions (version / edition)

Not version or edition dependent. Which DDL counts as irreversible comes from the behavior
catalog (`reversible=no` rows: `drop_table`, `drop_column`, `truncate_table`).
