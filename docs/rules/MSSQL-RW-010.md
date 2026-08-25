# MSSQL-RW-010 — Dropping a column does not reclaim its space

**Default severity:** Warning · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … DROP COLUMN` (each dropped column reported; `DROP COLUMN A, B` yields two
findings). Dropping a constraint is not this rule's concern.

## Why it matters

DROP COLUMN is **metadata-only** — fast, brief Sch-M — but the bytes the column occupied stay
physically inside every row until an index rebuild runs (or `DBCC CLEANTABLE`, which only helps
for a dropped variable-length or LOB column — see *How to fix*). Two practical
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

After the drop, reclaim the space in a maintenance window — but the two ways of doing that are
**not interchangeable**. Which one works depends on the type of the column that was dropped:

```sql
-- Dropped column was variable-length or LOB
-- (varchar, nvarchar, varbinary, text, ntext, image, sql_variant, xml, and their max variants):
DBCC CLEANTABLE (0, 'dbo.Orders');

-- Dropped column was fixed-length (int, bigint, datetime, char, decimal, uniqueidentifier, …):
ALTER INDEX ALL ON dbo.Orders REBUILD;  -- locking: MSSQL-LOCK-006
```

Microsoft is explicit about the limit: DBCC CLEANTABLE

> doesn't reclaim space after a fixed-length column is dropped.

Running it anyway is not merely useless — it is a fully logged operation that scans the table and
gives back nothing. For a fixed-length column, only the rebuild reclaims the bytes. CLEANTABLE is
also not supported on temporary tables, so when the target is a `#`/`##` table Planizer suggests
the rebuild alone.

The rule cannot tell the two cases apart: the dropped column's type is not in the script (the
column is gone by definition, and its declaration lives in some earlier migration), and offline
mode has no schema to look it up in. So the finding states both branches and leaves the one-line
decision to the reader, who knows what was dropped.

## Assumptions (version / edition)

Metadata-only with unreclaimed space on every version and edition (catalog row `drop_column`).
