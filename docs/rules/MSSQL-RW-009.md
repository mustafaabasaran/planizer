# MSSQL-RW-009 — Changing a column collation rewrites the column and needs dependent indexes dropped

**Default severity:** Critical · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ALTER COLUMN` with a `COLLATE` clause. The COLLATE keyword in the new
definition is certain offline.

## Why it matters

A collation change is a double hit. First, the column data is **rewritten** — collation
determines the binary comparison rules, so indexes and statistics built on the old collation
are invalid. Second, the statement **fails outright** (error 5074) while any index, constraint
(including PK/FK/CHECK referencing the column) or statistic depends on the column: they must
all be dropped first and recreated after. On a heavily indexed production table this silently
expands one ALTER into a multi-step, hours-long operation — and every recreated index is an
index build with its own locking profile (MSSQL-LOCK-002).

## Example

```sql
ALTER TABLE dbo.Orders ALTER COLUMN CustomerName nvarchar(200) COLLATE Latin1_General_100_CI_AS;
```

Reports: `Critical MSSQL-RW-009 … Changing the collation of column CustomerName on dbo.Orders
rewrites the column data; indexes, constraints and statistics that depend on the column must be
dropped first or the statement fails.`

The same ALTER without the COLLATE clause is not this rule's concern.

## How to fix

Inventory the dependents first (`sys.indexes` + `sys.index_columns`, `sys.check_constraints`,
`sys.stats`), script their drop/recreate around the ALTER, and schedule the whole sequence as
a rewrite. For large tables consider expand/contract: new column with the target collation,
batched backfill, swap. If the goal is per-query comparison behavior rather than a permanent
change, `COLLATE` in the query predicate avoids the DDL entirely.

## Assumptions (version / edition)

A rewrite on every version and edition (catalog row `alter_column_collation`).
