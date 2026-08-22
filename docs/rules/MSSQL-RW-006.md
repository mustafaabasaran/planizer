# MSSQL-RW-006 — Narrowing a column loses data and risks truncation failures

**Default severity:** Critical (reported Info + inconclusive offline) · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ALTER COLUMN` where the new definition carries an **explicit length or
precision** (`nvarchar(100)`, `decimal(9,2)`) — so the statement *could* be a narrowing
(`var(100)→var(50)`, precision drop). Offline the current type is unknown; the rule reports
**Info + inconclusive** alongside MSSQL-RW-005 rather than staying silent.

## Why it matters

Narrowing is the worst of the ALTER COLUMN family: a full rewrite under Sch-M, **plus** it
fails with a truncation error if any existing value does not fit — and if it succeeds, the
change is irreversible in the meaningful sense (widening back does not restore lost data,
see also MSSQL-REV-001's note). A reviewer needs the flag on any statement that *might* be one.

## Example

```sql
ALTER TABLE dbo.Orders ALTER COLUMN Notes nvarchar(100);
```

Reports: `Info MSSQL-RW-006 … Cannot compare nvarchar(100) with the current type of Notes
without schema; if this narrows the column, dbo.Orders is rewritten with data loss and
truncation failure risk. [inconclusive]`

Types that cannot narrow do not trigger it: parameterless (`bigint`) and MAX types carry no
explicit size.

## How to fix

Check the current length. If this really narrows, first prove no data is lost:

```sql
SELECT COUNT(*) FROM dbo.Orders WHERE LEN(Notes) > 100;   -- must be 0
```

then treat it as a rewrite (window or expand/contract). If it widens, it is metadata-only and
the finding can be suppressed with a reason.

## Assumptions (version / edition)

Not edition dependent. With a schema snapshot (Phase 2) confirmed narrowing reports as Critical
(catalog row `alter_column_narrow`); offline it stays Info + inconclusive.
