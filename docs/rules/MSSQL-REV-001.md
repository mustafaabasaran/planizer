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
SELECT * INTO dbo.SessionCache_backup FROM dbo.SessionCache;
-- verify, then delete in batches with a WHERE clause
```

## Assumptions (version / edition)

Not version or edition dependent. Which DDL counts as irreversible comes from the behavior
catalog (`reversible=no` rows: `drop_table`, `drop_column`, `truncate_table`).
