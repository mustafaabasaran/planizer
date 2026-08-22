# MSSQL-ENV-002 — Linked-server or cross-database reference ties the script to one environment

**Default severity:** Warning per statement (four-part names) · Info, one finding per file (three-part names) · **Category:** Transaction & script hygiene

## What it checks

Object names that reach outside the current database, anywhere in the statement — table
references, `EXEC` targets and scalar function calls (`Reporting.dbo.fn_FiscalYear(…)`) alike,
module bodies included (a view or procedure that binds to another database is exactly the
coupling this rule is about):

- **four-part names** `server.database.schema.object` go through a **linked server** — one
  Warning per statement, naming the server;
- **three-part names** `database.schema.object` — `[LookupDb].dbo.Currency`,
  `Reporting.dbo.usp_Rebuild` — depend on another database existing under exactly that name —
  one Info per file (the ADR-0001 pattern), anchored to the first such statement, with the count
  of statements, the databases involved and the first three examples. Statements carrying
  `-- planizer:ignore MSSQL-ENV-002` leave the count.

References to the system databases (`master`, `tempdb`, `msdb`, `model`) are ignored — they exist
on every instance. A name nested in `IF` / `BEGIN…END` / `WHILE` / `TRY` is counted **once**, for
the statement that contains it; the enclosing wrappers contribute only their own predicate, so a
linked-server reference inside `IF EXISTS (…) BEGIN … END` is one Warning, not three.

## Why it matters

A linked server is configuration that lives on one instance: the name, the credentials, the
provider, the firewall rule. A migration that reads through `[SRV-FX].[Market].dbo.Rates` works
only where somebody has set that up, and when it does work it runs as a **distributed query** —
MSDTC involvement, remote locks, remote statistics, a transaction that can no longer be rolled
back locally alone. A three-part name is lighter but equally environment-bound: the other
database must be on the same instance, under that name, with that schema, at deployment time. On
Azure SQL Database neither works at all. Both are the reason a script that passed every test
environment fails at the one customer whose database is called something else.

## Example

```sql
INSERT INTO dbo.Rates (Code, Rate) SELECT Code, Rate FROM [SRV-FX].[Market].dbo.Rates;
EXEC [SRV-FX].[Market].dbo.usp_Refresh;
```

Reports on each line: `Warning MSSQL-ENV-002 \`INSERT INTO dbo.Rates (Code, Rate) SELECT Code,
Rate FROM [SRV-FX].[Market].dbo…\` references linked server SRV-FX (SRV-FX.Market.dbo.Rates): the
script only works where that linked server is configured, and the remote access runs as a
distributed query.`

```sql
INSERT INTO dbo.Currency (Code) SELECT Code FROM [LookupDb].dbo.Currency;
UPDATE t SET t.Name = s.Name FROM dbo.Country t JOIN [LookupDb].dbo.Country s ON s.Code = t.Code;
SELECT Reporting.dbo.fn_FiscalYear(GETDATE());
EXEC Reporting.dbo.usp_Rebuild;
```

Reports once, on line 1: `Info MSSQL-ENV-002 4 statements in this file reference 2 other
databases by name (LookupDb, Reporting), e.g. LookupDb.dbo.Currency,
LookupDb.dbo.Country, Reporting.dbo.fn_FiscalYear: the script only runs where those
databases exist under exactly those names.`

Quiet: two-part names, temp tables, `master.dbo.spt_values`, `tempdb.sys.objects`.

## How to fix

Move the dependency out of the migration, or behind a name the environment owns:

```sql
-- created once per environment, outside the migration
CREATE SYNONYM dbo.LookupCurrency FOR [LookupDb].dbo.Currency;

-- the migration only knows the synonym
INSERT INTO dbo.Currency (Code) SELECT Code FROM dbo.LookupCurrency;
```

For linked-server reads, stage the remote data into a local table before the deployment and let
the migration read the local copy. When the cross-database reference is a deliberate,
documented part of the architecture, suppress with a reason:

```sql
-- planizer:ignore MSSQL-ENV-002 Lookup database is provisioned with every tenant
INSERT INTO dbo.Currency (Code) SELECT Code FROM [LookupDb].dbo.Currency;
```

## Assumptions (version / edition)

Not version or edition dependent. Offline the rule cannot know which linked servers or databases
an environment has; it reports the dependency, not its validity.
