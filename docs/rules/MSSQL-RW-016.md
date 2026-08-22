# MSSQL-RW-016 — Row width against the 8060-byte in-row limit

**Default severity:** Warning (Info + inconclusive for ADD COLUMN) · **Category:** Rewrite vs metadata-only

## What it checks

Fixed-width column arithmetic against SQL Server's **8060-byte** in-row limit:

- `CREATE TABLE` — the declared fixed-width columns are fully known offline; when their total
  exceeds 8060 bytes → **Warning**.
- `ALTER TABLE … ADD` of a fixed-width column — the current row width is unknown offline, so
  the rule reports **Info + inconclusive** with the byte count the column adds. This is the
  reference implementation of Planizer's *inconclusive* mechanism: no data, but the rule does
  not stay silent.

Variable-length types (`var*`, MAX, XML, …) are excluded from the arithmetic; the byte sizes
used are the storage sizes (`int` 4, `bigint` 8, `char(n)` n, `nchar(n)` 2n, `decimal(p)`
5–17, `datetime2(p)` 6–8, `uniqueidentifier` 16, `bit` rounded up to 1, …).

## Why it matters

A table whose fixed-width columns cannot fit in an 8060-byte row is not rejected at CREATE
time — SQL Server creates it with only a warning, and the failure arrives **later**, as error
511 on whichever `INSERT`/`UPDATE` first produces an oversized row. That is a production
time bomb with a migration-time defuse. The ADD COLUMN variant is the same bomb in slow
motion: each added fixed-width column quietly eats headroom that dropped columns
(MSSQL-RW-010) may not even have returned.

## Example

```sql
CREATE TABLE dbo.WideTable
(
    Id int NOT NULL,                 -- 4
    Payload char(8000) NOT NULL,     -- 8000
    Amount decimal(19, 4) NOT NULL,  -- 9
    CreatedAt datetime2 NOT NULL,    -- 8
    RowGuid uniqueidentifier NOT NULL, -- 16
    IsActive bit NOT NULL,           -- 1
    Extra char(50) NULL,             -- 50
    Comment nvarchar(1000) NULL      -- variable: excluded
);
```

Reports: `Warning MSSQL-RW-016 … Declared fixed-width columns of dbo.WideTable total 8088
bytes, exceeding the 8060-byte in-row limit; INSERT/UPDATE fails whenever a row cannot fit.`

```sql
ALTER TABLE dbo.Orders ADD LegacyCode char(50) NULL;
```

Reports: `Info MSSQL-RW-016 … Cannot verify the current row width of dbo.Orders offline;
adding LegacyCode (char(50)) grows each row by 50 bytes toward the 8060-byte in-row limit.
[inconclusive]`

## How to fix

Shrink or split the fixed-width columns, or switch rarely-filled wide `char`/`binary` columns
to variable-length types (`varchar`/`varbinary`), which can spill to row-overflow pages
instead of failing.

## Assumptions (version / edition)

Not version or edition dependent. With a schema provider (Phase 2) the ADD COLUMN branch checks
the real current row width instead of reporting inconclusively.
