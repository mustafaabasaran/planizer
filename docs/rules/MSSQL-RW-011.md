# MSSQL-RW-011 — Adding a PERSISTED computed column scans and writes the whole table

**Default severity:** Warning · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ADD <name> AS (<expression>) PERSISTED`. A computed column *without* PERSISTED
is metadata-only and does not trigger the rule.

## Why it matters

PERSISTED means the value is materialized on disk — and for a column added to an existing
table, that means computing and **writing the value for every existing row, under the ALTER
TABLE's Sch-M lock**: a full scan plus writes, with all access to the table blocked for the
duration. The non-persisted form costs nothing now and computes on read; persistence is only
*required* when the column needs an index (in some non-deterministic-precision cases) or when
read-time computation is measurably too expensive.

## Example

```sql
ALTER TABLE dbo.Orders
ADD TotalPrice AS (Quantity * UnitPrice) PERSISTED;
```

Reports: `Warning MSSQL-RW-011 … Adding PERSISTED computed column TotalPrice computes and
writes a value for every row of dbo.Orders under a Sch-M lock (full scan plus writes).`

The same ADD without `PERSISTED` is clean.

## How to fix

Add the column without PERSISTED (metadata-only, instant), and persist it later in a
maintenance window only if an index or measured performance requires it:

```sql
ALTER TABLE dbo.Orders ADD TotalPrice AS (Quantity * UnitPrice);
-- later, if genuinely needed:
-- ALTER TABLE dbo.Orders DROP COLUMN TotalPrice;
-- ALTER TABLE dbo.Orders ADD TotalPrice AS (Quantity * UnitPrice) PERSISTED;
```

## Assumptions (version / edition)

A full scan plus writes on every version and edition (catalog row `add_computed_persisted`).
