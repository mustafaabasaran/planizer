# MSSQL-RW-004 — Altering a column to a MAX type rewrites the column data

**Default severity:** Critical · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ALTER COLUMN` where the new type is a MAX type — `varchar(MAX)`,
`nvarchar(MAX)`, `varbinary(MAX)`.

## Why it matters

Widening a variable-length column within the `(n)` range (`varchar(50)` → `varchar(500)`) is
metadata-only. Going to **MAX** is not: MAX types use a different storage format (LOB
allocation units), so the conversion is a **size-of-data operation** — every value of the
column is rewritten under a Sch-M lock, no matter what the old type was. This is certain
offline, which is why this rule fires conclusively while the general type change
(MSSQL-RW-005) stays inconclusive. Bonus trap: the change is one-way in practice — altering
back from MAX to `(n)` is another full rewrite, plus truncation risk.

## Example

```sql
ALTER TABLE dbo.Orders ALTER COLUMN Notes nvarchar(MAX);
```

Reports: `Critical MSSQL-RW-004 … Altering column Notes of dbo.Orders to nvarchar(MAX) is a
size-of-data operation: the column data is rewritten under a Sch-M lock.`

`ALTER COLUMN Notes nvarchar(500)` does not trigger this rule (it is judged by
MSSQL-RW-005/006).

## How to fix

First ask whether MAX is actually needed — `nvarchar(4000)` covers most "just make it big"
cases as a metadata-only widen. If MAX is genuinely required on a large table, treat it like a
rewrite: schedule a window, or expand/contract (add a new MAX column, backfill in batches,
swap names in a later release).

## Assumptions (version / edition)

A rewrite on every version and edition (catalog row `alter_column_widen_to_max`).
