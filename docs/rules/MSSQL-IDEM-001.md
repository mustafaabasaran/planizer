# MSSQL-IDEM-001 — CREATE without an existence check is not re-runnable

**Default severity:** Warning · **Category:** Failure risk

## What it checks

`CREATE TABLE`, `CREATE INDEX` / `CREATE COLUMNSTORE INDEX`, `CREATE VIEW`, `CREATE PROCEDURE`,
`CREATE FUNCTION`, `CREATE TRIGGER`, `CREATE TYPE`, `CREATE SCHEMA` and `CREATE SEQUENCE`
statements that would fail if the object already existed. A CREATE counts as safe — and is not
reported — when any of the following holds:

- it sits (at any depth, through `BEGIN…END`) inside an `IF` whose predicate **queries the
  catalog**: an `EXISTS (…)` subquery, a catalog function (`OBJECT_ID`, `COL_LENGTH`, `TYPE_ID`,
  `SCHEMA_ID`, `INDEXPROPERTY`, `OBJECTPROPERTY`, …) or a `sys.*` / `INFORMATION_SCHEMA.*` /
  `sysobjects`-style view. Both branches count, so `IF EXISTS (…) PRINT 'exists' ELSE CREATE …`
  guards the ELSE. An `IF` on anything else (`IF @env = 'PROD'`) is not a guard;
- an **exit guard** precedes it in the same batch: an `IF` with such a catalog predicate whose
  THEN branch leaves the batch — `RETURN`, `THROW` or `GOTO`, directly or inside `BEGIN…END`
  (`IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL RETURN;` followed by a bare `CREATE TABLE`).
  `RETURN` ends the *batch*, so the guard covers everything after it up to the next `GO` — and
  nothing beyond it; one nested in another `IF`'s branch covers only that branch. `RAISERROR` is
  not an exit (the next statement still runs);
- it is `CREATE OR ALTER` (procedure, view, function, trigger) — idempotent by construction;
- an earlier statement **in the same file** dropped the same object safely: `DROP … IF EXISTS`,
  or a plain `DROP` that is itself guarded by such an `IF`;
- the target is a temp table (`#work`) — session-scoped, a fresh deployment never sees a previous
  run's copy — or the statement is `SELECT … INTO`.

Module bodies are not entered, so a `CREATE TABLE` written inside a procedure is not reported.

## Why it matters

The first run is never the problem; the second one is. A retried pipeline step, a migration
re-applied after a partial failure, an environment restored from a backup taken *after* the script
ran — each of them hits the bare `CREATE TABLE` and stops with **error 2714, "There is already an
object named 'Orders' in the database"** (indexes: **error 1913**). Everything after it — often
exactly the part meant to complete the earlier partial run — never executes. Idempotent DDL is what
lets a migration runner be restarted without somebody editing the script by hand first.

## Example

```sql
CREATE TABLE dbo.Orders (Id int NOT NULL PRIMARY KEY, Total money NULL);
```

Reports: `Warning MSSQL-IDEM-001 CREATE TABLE dbo.Orders is not guarded by an existence check;
running the script a second time fails because the table already exists (error 2714).` with the
fix `Guard it: IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL BEGIN CREATE TABLE dbo.Orders … END`.

The other object kinds get a guard that fits them: `CREATE INDEX IX_Orders_Total ON dbo.Orders
(Total)` → `IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Total' AND
object_id = OBJECT_ID(N'dbo.Orders'))`; `CREATE TYPE dbo.IdList` → `IF TYPE_ID(N'dbo.IdList') IS
NULL`; `CREATE SEQUENCE dbo.OrderNumbers` → `IF OBJECT_ID(N'dbo.OrderNumbers', N'SO') IS NULL`;
and a module such as `CREATE PROCEDURE dbo.GetOrders` → `Use CREATE OR ALTER PROCEDURE
dbo.GetOrders so the script can be re-run.`

These are quiet:

```sql
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders (Id int NOT NULL PRIMARY KEY);
END

IF EXISTS (SELECT 1 FROM sys.types WHERE name = N'IdList' AND schema_id = SCHEMA_ID(N'dbo'))
    PRINT 'type exists';
ELSE
    CREATE TYPE dbo.IdList AS TABLE (Id int NOT NULL);

DROP TABLE IF EXISTS dbo.Staging;
CREATE TABLE dbo.Staging (Id int NOT NULL);
```

## How to fix

Three forms, depending on the object:

```sql
-- tables, indexes, types, sequences: ask the catalog first
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders (Id int NOT NULL PRIMARY KEY, Total money NULL);
END

-- procedures, views, functions, triggers (SQL Server 2016 SP1 and later)
CREATE OR ALTER PROCEDURE dbo.GetOrders AS SELECT Id FROM dbo.Orders;

-- CREATE SCHEMA must be alone in its batch, so the guarded form goes through EXEC
IF SCHEMA_ID(N'audit') IS NULL EXEC(N'CREATE SCHEMA audit');
```

Drop-then-create (`DROP TABLE IF EXISTS dbo.Staging; CREATE TABLE dbo.Staging (…)`) is the other
accepted pattern — right for staging and helper objects, wrong for tables that hold data.

## Assumptions (version / edition)

The fix text follows `--target-version`: `CREATE OR ALTER` is proposed for modules from 2017 on;
at exactly 2016 it is proposed with the SP1 caveat plus a drop-first alternative (`IF
OBJECT_ID(N'dbo.GetOrders', N'P') IS NOT NULL DROP PROCEDURE dbo.GetOrders; GO`); below 2016 only
the drop-first form is offered. Edition independent. Offline the rule cannot know whether the
object already exists in production; it reports what a re-run would do.
