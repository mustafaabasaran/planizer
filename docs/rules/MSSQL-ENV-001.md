# MSSQL-ENV-001 — USE [database] overrides the migration runner's target database

**Default severity:** Info · **Category:** Transaction & script hygiene

## What it checks

A `USE <database>` statement anywhere in the script (top level or nested). Each occurrence is
reported.

## Why it matters

Migration runners — EF Core, Flyway, Liquibase, DbUp, `sqlcmd -d`, a deployment pipeline's
connection string — already connect to the database the script is meant for. A `USE` inside the
script **overrides that choice** with a hard-coded name. Where the name is the same everywhere it
is harmless noise; where it differs per environment (`Accounting_Dev` in development,
`ACC_PROD_01` at the customer, `tenant-xyz` in a multi-tenant setup) the script either fails with
**error 911, "Database 'X' does not exist"** or — worse — runs happily against a database of that
name that is not the one being deployed. On Azure SQL Database `USE` is not supported at all
(error 40508).

## Example

```sql
USE [Accounting_Dev];
GO
CREATE TABLE dbo.T (Id int NOT NULL);
```

Reports on line 1: `Info MSSQL-ENV-001 USE Accounting_Dev pins the script to a database name:
the migration runner already connects to the target database, and on an environment where the
name differs the script fails or runs against the wrong database.`

## How to fix

Delete the `USE` (and the `GO` that usually follows it) and let the runner's connection choose
the database:

```sql
CREATE TABLE dbo.T (Id int NOT NULL);
```

If the script is also run by hand from SSMS, the database selector in the toolbar does the same
job.

## Assumptions (version / edition)

Not version or edition dependent; on `--target-version azure` the statement would fail outright,
but the rule keeps its Info severity because the name-pinning problem is the same everywhere.
