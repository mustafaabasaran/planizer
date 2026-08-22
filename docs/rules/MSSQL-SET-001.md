# MSSQL-SET-001 — Filtered index / PERSISTED computed column needs QUOTED_IDENTIFIER and ANSI_NULLS ON

**Default severity:** Blocker when the script switched an option OFF · Warning (inconclusive) when the script never sets it · **Category:** Transaction & script hygiene

## What it checks

Statements that SQL Server only executes while `QUOTED_IDENTIFIER` and `ANSI_NULLS` are both ON:

- a **filtered index** — `CREATE INDEX … WHERE …`, including an inline `INDEX … WHERE …` in
  `CREATE TABLE`;
- a **PERSISTED computed column** — `ALTER TABLE … ADD c AS (…) PERSISTED` or the same inside
  `CREATE TABLE`.

The state of the two options is tracked per file in script order, across `GO` (they belong to the
session): `SET QUOTED_IDENTIFIER ON|OFF`, `SET ANSI_NULLS ON|OFF`, the combined `SET ANSI_NULLS,
QUOTED_IDENTIFIER ON` and `SET ANSI_DEFAULTS ON|OFF` (which flips both) are all understood; the
last setting wins. At each requiring statement:

- an option explicitly **OFF** → Blocker: the statement fails with **error 1934** whatever the
  client does;
- an option **never set** → Warning, marked inconclusive: the outcome depends on the connection
  defaults;
- both explicitly ON → quiet.

A plain index or a non-persisted computed column is never reported, even under an explicit OFF.

## Why it matters

The error is real and specific: **1934, "CREATE INDEX failed because the following SET options
have incorrect settings: 'QUOTED_IDENTIFIER'"**. Whether it fires depends on something the script
does not show: the connection. SSMS and the .NET client turn both options ON; **sqlcmd and osql
run with `QUOTED_IDENTIFIER OFF`** unless `-I` is passed, and ODBC/OLE DB connections can be
configured either way. So the migration works from the developer's SSMS, works from the CI job
that happens to use `Invoke-Sqlcmd`, and fails from the deployment agent that shells out to
`sqlcmd`. Stating the options in the script removes the dependency.

## Example

```sql
SET QUOTED_IDENTIFIER OFF;
GO
CREATE INDEX IX_Orders_Open ON dbo.Orders (CustomerId) WHERE Status = 'Open';
```

Reports on line 3: `Blocker MSSQL-SET-001 Filtered index IX_Orders_Open on dbo.Orders requires
QUOTED_IDENTIFIER ON, but this script switched QUOTED_IDENTIFIER OFF earlier (QUOTED_IDENTIFIER
at line 1): the statement fails with error 1934.`

Without any `SET` in the file, the same `CREATE INDEX` reports `Warning MSSQL-SET-001 Filtered
index IX_Orders_Open on dbo.Orders requires QUOTED_IDENTIFIER and ANSI_NULLS ON; the script never
sets QUOTED_IDENTIFIER and ANSI_NULLS explicitly, so the outcome depends on the connection
defaults — sqlcmd and osql run with QUOTED_IDENTIFIER OFF unless -I is given, and the statement
then fails with error 1934. [inconclusive]`. `SET ANSI_NULLS OFF; ALTER TABLE dbo.Orders ADD
TotalWithTax AS (Total * 1.2) PERSISTED;` is a Blocker on the PERSISTED column.

Quiet:

```sql
SET ANSI_NULLS, QUOTED_IDENTIFIER ON;
GO
CREATE INDEX IX_Orders_Open ON dbo.Orders (CustomerId) WHERE Status = 'Open';
```

## How to fix

Put both options at the top of the script and do not switch them off later:

```sql
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
CREATE INDEX IX_Orders_Open ON dbo.Orders (CustomerId) WHERE Status = 'Open';
```

## Assumptions (version / edition)

Not version or edition dependent. The Warning form is marked inconclusive because the connection
defaults are not visible offline; the rule cannot tell a job that always runs from SSMS apart
from one that runs from `sqlcmd` without `-I`. The same options matter for indexed views and
indexes on computed columns, which are not yet in the rule's scope.
