# MSSQL-REV-003 — sp_rename leaves dependent objects pointing at the old name

**Default severity:** Warning (Critical for column renames) · **Category:** Reversibility

## What it checks

Every `EXEC sp_rename …` call. A **column** rename (`@objtype = 'COLUMN'`, positional or
named) reports as **Critical**; table and other object renames report as **Warning**.

## Why it matters

`sp_rename` renames the object in the catalog and *nothing else*. Because of deferred name
resolution, procedures, views and functions that reference the old name still compile — and
fail at **runtime**, the first time someone calls them after the rename. The migration itself
succeeds; production breaks later, which is the worst possible failure mode. Column renames are
the most dangerous variant: queries, index definitions and computed columns all reference
column names.

## Example

```sql
EXEC sp_rename 'dbo.Orders.CustName', 'CustomerName', 'COLUMN';
EXEC sp_rename 'dbo.OldOrders', 'ArchivedOrders';
```

Line 1 reports **Critical**, line 2 **Warning**: `… is not updated inside dependent
procedures, views or functions (deferred name resolution); they still reference the old name
and fail at runtime.`

An ordinary procedure call (`EXEC dbo.RecalculateTotals …`) is not a rename and does not
trigger the rule.

## How to fix

Prefer **expand/contract**: create the new name (new column backfilled from the old one, or a
view/synonym under the old name), migrate readers release by release, drop the old name later.
At minimum, check what references the object before renaming:

```sql
SELECT referencing_schema_name, referencing_entity_name
FROM sys.dm_sql_referencing_entities('dbo.Orders', 'OBJECT');
-- or query sys.sql_expression_dependencies
```

## Assumptions (version / edition)

Not version or edition dependent. Offline the dependency tree is unknown — with a schema
snapshot (Phase 2) this rule can name the actual dependents.
