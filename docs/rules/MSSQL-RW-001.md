# MSSQL-RW-001 — Adding a nullable column without a default is metadata-only

**Default severity:** Info · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ADD` of a nullable column with no default. This is the safe way to add a column:
a **metadata-only** change — existing rows are not touched, regardless of table size.

This is an explanatory "safe" finding: the report says so out loud, so the reviewer knows the
statement was analyzed and is harmless, rather than guessing from silence.

## Why it matters

Migration reviews spend most of their time proving statements *safe*, not finding dangerous
ones. A positive "this is metadata-only" answer is exactly what a reviewer (or a CI gate)
needs to approve quickly — and it is the baseline against which the expensive variants
(MSSQL-RW-002, MSSQL-RW-003) are judged.

## Example

```sql
ALTER TABLE dbo.Orders ADD Notes nvarchar(500) NULL;
ALTER TABLE dbo.Orders ADD ExternalRef int;
```

Both report: `Info MSSQL-RW-001 … Adding nullable column … (no default) to dbo.Orders is
metadata-only; existing rows are not touched — safe.` (A column with no nullability clause is
nullable by default.)

`NOT NULL` + `DEFAULT` is MSSQL-RW-002 territory; a computed column is not a nullable data
column and is not flagged here.

## How to fix

Nothing to fix — this is the recommended pattern. When a column must eventually be NOT NULL,
add it nullable first (this rule), backfill in batches, then alter it to NOT NULL
(see MSSQL-RW-007 for what that step costs).

## Assumptions (version / edition)

Metadata-only on every version and edition (catalog row `add_column_nullable`). Like all
catalog-driven rules, a missing catalog row for the configured target produces an inconclusive
Warning instead of silence.
