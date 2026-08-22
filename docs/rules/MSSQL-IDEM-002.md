# MSSQL-IDEM-002 — ALTER TABLE ADD/DROP without an existence check is not re-runnable

**Default severity:** Warning · **Category:** Failure risk

## What it checks

`ALTER TABLE … ADD <column>`, `ADD CONSTRAINT`, `DROP COLUMN` and `DROP CONSTRAINT` statements
whose element would already be present (ADD) or already be gone (DROP) on a second run. The
statement is not reported when:

- it sits inside a catalog-querying `IF` (the same heuristic as MSSQL-IDEM-001):
  `IF COL_LENGTH(N'dbo.Orders', N'Status') IS NULL`, `IF NOT EXISTS (SELECT 1 FROM sys.columns …)`,
  `IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = …)`, …;
- an **exit guard** precedes it in the same batch — `IF EXISTS (SELECT 1 FROM sys.columns …)
  RETURN;` / `IF COL_LENGTH(…) IS NOT NULL RETURN;` followed by the bare `ALTER TABLE` (same rules
  as MSSQL-IDEM-001: `RETURN` / `THROW` / `GOTO`, same batch, same scope);
- the element carries its own `IF EXISTS`: `DROP COLUMN IF EXISTS`, `DROP CONSTRAINT IF EXISTS`
  (SQL Server 2016+);
- for an ADD — an earlier statement in the same file safely dropped the same column or constraint
  of the same table (`DROP COLUMN IF EXISTS StatusNew; … ADD StatusNew …`);
- for a DROP — an earlier statement in the same file **added** the element: the helper-column
  pattern (add `TmpTotal` guarded, backfill, drop `TmpTotal`). A re-run re-adds it before
  dropping it, so the bare DROP is safe;
- the table is a temp table or a table variable.

One finding per statement, naming every unguarded element it adds or drops.

## Why it matters

An `ALTER TABLE … ADD` is the statement most often re-run by hand after a long migration stopped
half-way, and the one most often hit by a retried pipeline. The second execution fails with
**error 2705, "Column names in each table must be unique"** (ADD column), **2714** (ADD CONSTRAINT
with an existing name), **4924, "column 'LegacyCode' does not exist in table 'Orders'"** (DROP
COLUMN) or **3728, "'FK_Orders_Customers' is not a constraint"** (DROP CONSTRAINT) — and the
statements behind it never run.

An **unnamed** constraint is the exception that is worse, not better: `ADD CHECK (Total >= 0)`,
`ADD FOREIGN KEY (…) REFERENCES …` and `ADD UNIQUE (…)` do *not* fail on the second run — SQL
Server generates a fresh `CK__Orders__…` name every time and silently adds a **duplicate**
constraint (a second one to maintain on every write, a second index for the UNIQUE). Only an
unnamed `PRIMARY KEY` (**error 1779**, the table already has one) and an unnamed `DEFAULT`
(**error 1781**, the column already has one) fail. The finding's wording follows the kind.

## Example

```sql
ALTER TABLE dbo.Orders ADD Status tinyint NOT NULL CONSTRAINT DF_Orders_Status DEFAULT 0;
```

Reports: `Warning MSSQL-IDEM-002 ALTER TABLE dbo.Orders ADD column Status is not guarded by an
existence check; running the script a second time fails because the column already exists (error
2705).` with the fix `Guard it: IF COL_LENGTH(N'dbo.Orders', N'Status') IS NULL ALTER TABLE
dbo.Orders ADD Status tinyint NOT NULL CONSTRAINT DF_Orders_Status DEFAULT 0;`

The DROP side: `ALTER TABLE dbo.Orders DROP COLUMN LegacyCode;` → `… DROP column LegacyCode is not
guarded by an existence check; running the script a second time fails because the column no
longer exists (error 4924).` with the fix `Use ALTER TABLE dbo.Orders DROP COLUMN IF EXISTS
LegacyCode;`

These are quiet:

```sql
IF COL_LENGTH(N'dbo.Orders', N'Status') IS NULL
    ALTER TABLE dbo.Orders ADD Status tinyint NULL;

ALTER TABLE dbo.Orders DROP CONSTRAINT IF EXISTS FK_Orders_Customers;

-- helper column: guarded ADD, backfill, bare DROP
IF COL_LENGTH(N'dbo.Orders', N'TmpTotal') IS NULL
    ALTER TABLE dbo.Orders ADD TmpTotal money NULL;
UPDATE dbo.Orders SET TmpTotal = Total WHERE TmpTotal IS NULL;
ALTER TABLE dbo.Orders DROP COLUMN TmpTotal;
```

## How to fix

```sql
-- ADD column
IF COL_LENGTH(N'dbo.Orders', N'Status') IS NULL
    ALTER TABLE dbo.Orders ADD Status tinyint NOT NULL CONSTRAINT DF_Orders_Status DEFAULT 0;

-- ADD CONSTRAINT (name it — an unnamed constraint cannot be checked for)
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = N'CK_Orders_Total' AND parent_object_id = OBJECT_ID(N'dbo.Orders'))
    ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Orders_Total CHECK (Total >= 0);

-- DROP COLUMN / DROP CONSTRAINT (2016+)
ALTER TABLE dbo.Orders DROP COLUMN IF EXISTS LegacyCode;
ALTER TABLE dbo.Orders DROP CONSTRAINT IF EXISTS FK_Orders_Customers;

-- DROP on SQL Server 2014
IF COL_LENGTH(N'dbo.Orders', N'LegacyCode') IS NOT NULL
    ALTER TABLE dbo.Orders DROP COLUMN LegacyCode;
```

## Assumptions (version / edition)

The DROP fix proposes `IF EXISTS` from `--target-version 2016` on and a `COL_LENGTH` /
`sys.objects` guard below that. Edition independent. Unnamed constraints get a system-generated
name that cannot be checked for: for CHECK / FOREIGN KEY / UNIQUE the message says a re-run
*duplicates* the constraint and the fix asks you to name it first; an unnamed PRIMARY KEY is
guarded with `OBJECTPROPERTY(OBJECT_ID(…), 'TableHasPrimaryKey') = 0`, an unnamed DEFAULT through
`sys.default_constraints` on the column.
