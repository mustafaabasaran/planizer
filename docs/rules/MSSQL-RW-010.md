# MSSQL-RW-010 — Dropping a column does not reclaim its space

**Default severity:** Warning · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … DROP COLUMN` (each dropped column reported; `DROP COLUMN A, B` yields two
findings). Dropping a constraint is not this rule's concern.

## Why it matters

DROP COLUMN is **metadata-only** — fast, brief Sch-M — but the bytes the column occupied stay
physically inside every row until `DBCC CLEANTABLE` or an index rebuild runs. Two practical
consequences: the disk space you expected back does not appear, and (subtler) the dead weight
still counts toward the 8060-byte row limit, so a later ADD COLUMN can fail on a table that
looks like it has room. The *irreversibility* of the drop is the separate, louder finding
MSSQL-REV-001; this rule covers the operational surprise that remains even when dropping is
the right call.

## Example

```sql
ALTER TABLE dbo.Orders DROP COLUMN LegacyCode;
```

Reports: `Warning MSSQL-RW-010 … Dropping column LegacyCode from dbo.Orders is metadata-only,
but its space is not reclaimed until DBCC CLEANTABLE or an index rebuild runs.`

`ALTER TABLE dbo.Orders DROP CONSTRAINT DF_Orders_Status;` does not trigger it.

## How to fix

After the drop, reclaim the space in a maintenance window:

```sql
DBCC CLEANTABLE (0, 'dbo.Orders');
-- or: ALTER INDEX <clustered index> ON dbo.Orders REBUILD;  (locking: MSSQL-LOCK-006)
```

CLEANTABLE only reclaims variable-length and LOB bytes; fixed-width remnants need the rebuild.

## Assumptions (version / edition)

Metadata-only with unreclaimed space on every version and edition (catalog row `drop_column`).
