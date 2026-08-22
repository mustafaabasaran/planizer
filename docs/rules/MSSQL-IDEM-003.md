# MSSQL-IDEM-003 — DROP without IF EXISTS is not re-runnable

**Default severity:** Warning · **Category:** Failure risk

## What it checks

`DROP TABLE`, `DROP INDEX` (both `DROP INDEX ix ON t` and the legacy `DROP INDEX t.ix`), `DROP VIEW`,
`DROP PROCEDURE`, `DROP FUNCTION`, `DROP TRIGGER`, `DROP TYPE`, `DROP SEQUENCE` and `DROP SCHEMA`
that would fail if the object were not there. Not reported when:

- the statement has an `IF EXISTS` clause (SQL Server 2016+);
- it sits inside a catalog-querying `IF` — `IF OBJECT_ID(N'dbo.Legacy', N'U') IS NOT NULL`,
  `IF EXISTS (SELECT 1 FROM sys.indexes …)`, `IF TYPE_ID(…) IS NOT NULL`, `IF SCHEMA_ID(…) IS NOT
  NULL` (the same heuristic as MSSQL-IDEM-001);
- an **exit guard** precedes it in the same batch — `IF OBJECT_ID(N'dbo.Legacy', N'U') IS NULL
  RETURN;` followed by the bare `DROP` (same rules as MSSQL-IDEM-001: `RETURN` / `THROW` / `GOTO`,
  same batch, same scope);
- the same file **created** the object earlier — the staging pattern (`CREATE TABLE dbo.Staging`
  or `SELECT … INTO dbo.Staging`, load, `DROP TABLE dbo.Staging`): a re-run recreates it before
  dropping it;
- the target is a temp table.

A `DROP TABLE a, b` with several objects produces one finding listing the unguarded ones.

## Why it matters

The object may be gone for a good reason: the script already ran once, or this environment never
had it — a cleanup written against a development database that still carried the legacy object,
applied to a freshly provisioned customer environment. Either way the statement fails with
**error 3701, "Cannot drop the table 'dbo.Legacy', because it does not exist or you do not have
permission"** (**15151** for a schema), and the migration runner stops there.

## Example

```sql
DROP TABLE dbo.Legacy;
```

Reports: `Warning MSSQL-IDEM-003 DROP TABLE dbo.Legacy is not guarded by an existence check;
running the script when the table is already gone fails (error 3701).` with the fix `Use DROP
TABLE IF EXISTS dbo.Legacy;`

With `--target-version 2014` the same statement gets the pre-2016 fix instead: `Guard it (DROP …
IF EXISTS needs SQL Server 2016): IF OBJECT_ID(N'dbo.Legacy', N'U') IS NOT NULL DROP TABLE
dbo.Legacy;`. A `DROP INDEX IX_Orders_Total ON dbo.Orders;` is fixed as `Use DROP INDEX IF EXISTS
IX_Orders_Total ON dbo.Orders;`.

These are quiet:

```sql
DROP TABLE IF EXISTS dbo.Legacy;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Total' AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    DROP INDEX IX_Orders_Total ON dbo.Orders;
END

IF OBJECT_ID(N'dbo.Staging', N'U') IS NULL
    CREATE TABLE dbo.Staging (Id int NOT NULL);
INSERT INTO dbo.Staging (Id) SELECT Id FROM dbo.Orders;
DROP TABLE dbo.Staging;          -- created above: a re-run recreates it first
```

## How to fix

```sql
-- SQL Server 2016 and later
DROP TABLE IF EXISTS dbo.Legacy;
DROP PROCEDURE IF EXISTS dbo.GetOrders;
DROP INDEX IF EXISTS IX_Orders_Total ON dbo.Orders;

-- SQL Server 2014
IF OBJECT_ID(N'dbo.Legacy', N'U') IS NOT NULL DROP TABLE dbo.Legacy;
IF OBJECT_ID(N'dbo.GetOrders', N'P') IS NOT NULL DROP PROCEDURE dbo.GetOrders;
```

## Assumptions (version / edition)

The fix proposes `DROP … IF EXISTS` from `--target-version 2016` on and an `OBJECT_ID` /
`sys.indexes` / `TYPE_ID` / `SCHEMA_ID` guard below that (MSSQL-VER-001 reports `IF EXISTS`
itself when the target is older than 2016, so the two rules never contradict each other).
Edition independent. Offline the rule cannot know whether the object exists in production; it
reports what a run without it would do.
