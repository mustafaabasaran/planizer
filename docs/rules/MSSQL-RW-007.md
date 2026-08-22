# MSSQL-RW-007 — Altering a column to NOT NULL scans the whole table and fails on NULLs

**Default severity:** Critical · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ALTER COLUMN` whose new definition says `NOT NULL`. Writing NOT NULL in the new
definition is the NULL→NOT NULL intent — certain offline, unlike the type-change ambiguity of
MSSQL-RW-005.

## Why it matters

SQL Server must prove no NULL exists before it accepts the constraint: a **full scan of the
table under a Sch-M lock** — all reads and writes blocked for the duration, minutes on a large
table. And if even one NULL remains, the statement fails (error 515) *after* holding that lock
for the whole scan. This is also the final step of the recommended expand/contract pattern for
adding NOT NULL columns — the step whose cost teams forget to budget.

## Example

```sql
ALTER TABLE dbo.Orders ALTER COLUMN CustomerId int NOT NULL;
```

Reports: `Critical MSSQL-RW-007 … Altering column CustomerId of dbo.Orders to NOT NULL scans
the entire table to validate under a Sch-M lock and fails if any NULL exists.`

`ALTER COLUMN CustomerId int NULL` is the opposite direction (MSSQL-RW-008, safe).

## How to fix

Backfill first, so the validation scan cannot fail:

```sql
UPDATE dbo.Orders SET CustomerId = <default> WHERE CustomerId IS NULL;
-- (batch it on a large table — see MSSQL-LOCK-009)
ALTER TABLE dbo.Orders ALTER COLUMN CustomerId int NOT NULL;
```

The scan itself is unavoidable; schedule it. On 2012+ a `NOT NULL` **added as a CHECK
constraint WITH NOCHECK** avoids the scan at the price of an untrusted constraint
(see MSSQL-RW-014's trade-off).

## Assumptions (version / edition)

A validating full scan on every version and edition (catalog row
`alter_column_null_to_notnull`, `full_scan`).
