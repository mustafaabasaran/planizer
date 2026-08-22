# MSSQL-BATCH-001 — Column added in the same batch is referenced before GO

**Default severity:** Blocker · **Category:** Failure risk

## What it checks

A column that `ALTER TABLE … ADD` introduces — or that `EXEC sp_rename … , 'COLUMN'` gives its new
name — is referenced by a **later statement of the same batch** (no `GO` in between). Statements
nested in `IF` / `BEGIN…END` / `WHILE` / `TRY` bodies belong to the batch like any other.

Only references that SQL Server binds at compile time count: DML (`SELECT`, `INSERT` column lists,
`UPDATE … SET`, `DELETE`, `MERGE`), `IF` / `WHILE` predicates, `DECLARE … =` / `SET @v =`
initialisers. DDL on the new column (`CREATE INDEX`, `ADD CONSTRAINT`, `ALTER COLUMN`) binds at
execution and is fine; dynamic SQL (`EXEC sp_executesql N'…'`) compiles when it runs; and a table
**created in the same batch** gets deferred name resolution for every statement that uses it, so
its columns are excluded too.

Matching is by column name plus table: the statement has to name the column's table somewhere,
and a qualified reference must point at that table — directly (`Orders.Status`, `dbo.Orders.Status`)
or through an alias that resolves to it in the statement's own FROM clause (`o.Status` with
`FROM dbo.Orders o`). An alias of a different table, another table's name, or the alias of a
derived table, CTE, table variable or table-valued function in the statement's own FROM clause
(`x.Flag` with `CROSS APPLY (SELECT o.Id AS Flag) x`, `v.Flag` with `JOIN @t v`) is not a match —
it names a column of *that* row source. A qualifier that cannot be resolved at all (an alias from
an outer scope) is treated as a match, on purpose — a missed compile error is worse than one extra
Blocker to review.

## Why it matters

SQL Server compiles the **whole batch** before running its first statement. The `UPDATE` that
follows the `ALTER TABLE` is bound against the table as it exists *before* the ALTER, so the batch
fails with **error 207, "Invalid column name"** — even though, read top to bottom, the script looks
correct. Wrapping the ALTER in `IF COL_LENGTH(…) IS NULL` does not help: the first run, the one
that matters, is exactly the one where the column does not exist yet.

## Example

```sql
ALTER TABLE dbo.Orders ADD Status tinyint NOT NULL CONSTRAINT DF_Orders_Status DEFAULT (0);
UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;
```

Reports on the UPDATE: `Blocker MSSQL-BATCH-001 This statement references column
dbo.Orders.Status (added at line 1) in the same batch; a batch is compiled as a whole before any
statement in it runs, so the column does not exist yet at compile time and the batch fails with
error 207 (Invalid column name 'Status').`

The rename variant: `EXEC sp_rename 'dbo.Customers.Fax', 'FaxNumber', 'COLUMN';` followed by
`SELECT FaxNumber FROM dbo.Customers;` in the same batch reports `… dbo.Customers.FaxNumber
(renamed at line 1) …`.


When the `ALTER TABLE … ADD` (or the `sp_rename`) is itself guarded by a catalog check — `IF
COL_LENGTH(...) IS NULL`, an `IF NOT EXISTS (__EFMigrationsHistory …)` block — the message adds
that the failure is **environment-dependent**: the batch fails with error 207 on any database where
the column does not exist yet (a fresh environment, a first deployment) and only compiles where an
earlier run already added it. That is how such scripts survive incrementally deployed
environments and then break on a new one; the severity stays Blocker.

## How to fix

Put `GO` after the ALTER so the reference compiles in a later batch:

```sql
ALTER TABLE dbo.Orders ADD Status tinyint NOT NULL CONSTRAINT DF_Orders_Status DEFAULT (0);
GO
UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;
```

Where the migration runner does not understand `GO` (it is a client-tool separator, not T-SQL),
run the referencing statement as dynamic SQL so it is compiled after the ALTER has executed:

```sql
EXEC sp_executesql N'UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;';
```

## Assumptions (version / edition)

Not version or edition dependent — batch compilation works the same on every SQL Server.
Offline the rule cannot know whether the column already exists in production (in which case the
batch would compile); it reports the first-run behaviour, which is what a migration must survive.
A derived table that re-exposes the new column (`FROM (SELECT * FROM dbo.Orders) x` then
`x.Status`) is not followed through its alias and is missed; reference the table directly or put
the `GO` in regardless.
