# MSSQL-RW-009 — Changing a column collation may convert the data and needs dependent objects dropped

**Default severity:** Warning (inconclusive offline) · **Category:** Rewrite vs metadata-only

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

Reports: `Warning MSSQL-RW-009 … Changing the collation of column CustomerName on dbo.Orders
is metadata-only while the code page stays the same, but a varchar column moving to a different
code page is converted (size-of-data), and indexes, constraints or statistics depending on the
column must be
dropped first or the statement fails.`

The same ALTER without the COLLATE clause is not this rule's concern.

## How to fix

Inventory the dependents first (`sys.indexes` + `sys.index_columns`, `sys.check_constraints`,
`sys.stats`), script their drop/recreate around the ALTER, and schedule the whole sequence as
a rewrite. For large tables consider expand/contract: new column with the target collation,
batched backfill, swap. If the goal is per-query comparison behavior rather than a permanent
change, `COLLATE` in the query predicate avoids the DDL entirely.

## Assumptions (version / edition)

Not version or edition dependent (catalog row `alter_column_collation`). CI measurement against a
real SQL Server showed the collation swap alone writes only metadata (≈0.5 KB of log on a 100k-row
table); the size-of-data conversion happens when a varchar column crosses code pages, which cannot
be decided offline — hence the inconclusive Warning rather than a hard Critical.
