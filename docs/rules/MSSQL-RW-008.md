# MSSQL-RW-008 — Altering a column to NULL is metadata-only

**Default severity:** Info · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ALTER COLUMN` whose new definition explicitly says `NULL`. Writing NULL is the
NOT NULL→NULL intent — certain offline. Dropping a NOT NULL constraint requires no validation
and touches no data: **metadata-only**.

Like MSSQL-RW-001, this is an explanatory "safe" finding: the reviewer sees the statement was
analyzed and is harmless.

## Why it matters

The two directions of nullability look symmetric in syntax and are wildly asymmetric in cost:
to NOT NULL is a validating full scan under Sch-M (MSSQL-RW-007); to NULL is free. A report
that states the cheap direction explicitly saves the reviewer from assuming the worst.

## Example

```sql
ALTER TABLE dbo.Orders ALTER COLUMN LegacyCode int NULL;
```

Reports: `Info MSSQL-RW-008 … Altering column LegacyCode of dbo.Orders to NULL is
metadata-only; no data is touched — safe.`

The opposite (`… int NOT NULL`) is MSSQL-RW-007, Critical.

## How to fix

Nothing to fix. One planning note: this direction is trivially cheap, but reversing it later
(back to NOT NULL) costs the full validation scan — drop nullability constraints only when
they are genuinely wrong.

## Assumptions (version / edition)

Metadata-only on every version and edition (catalog row `alter_column_notnull_to_null`).
