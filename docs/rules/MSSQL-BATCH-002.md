# MSSQL-BATCH-002 — Variable declared in an earlier batch is used after GO

**Default severity:** Blocker · **Category:** Failure risk

## What it checks

A variable — scalar (`DECLARE @tenant int`) or table (`DECLARE @ids TABLE (…)`) — declared in one
batch of a file is referenced in a **later batch of the same file** without being declared again
there. Uses nested in `IF` / `WHILE` / `TRY` bodies count; module bodies (`CREATE PROCEDURE` …) are
never walked. Named arguments of a procedure call (`EXEC sp_rename @objname = …`) are the
callee's parameter names, not variable uses; `@@ROWCOUNT`-style system functions are not
variables. Declaration order inside a batch is not checked, and a variable that is never declared
anywhere is a plain typo, not this rule.

## Why it matters

`GO` is not T-SQL: the client tool splits the script there and sends each piece as its own batch.
Every variable lives exactly as long as its batch, so the next batch fails to compile with
**error 137, "Must declare the scalar variable"** (or **1087, "Must declare the table variable"**) —
before a single statement of it runs. In a migration this typically means the first batch (the
schema change) is already applied and the second (the backfill) never happens.

## Example

```sql
DECLARE @tenant int = 1;
UPDATE dbo.Settings SET Value = N'x' WHERE TenantId = @tenant;
GO
DELETE FROM dbo.Cache WHERE TenantId = @tenant;
```

Reports on the DELETE: `Blocker MSSQL-BATCH-002 @tenant is used here but was declared at line 1
in an earlier batch; GO ends the scope of every variable, so this batch fails to compile with
error 137 (Must declare the scalar variable "@tenant").` Several out-of-scope variables in one
statement produce one finding listing them all.

## How to fix

Re-declare the variable in the batch that uses it (the fix text repeats the original `DECLARE`
so it can be pasted):

```sql
DECLARE @tenant int = 1;
UPDATE dbo.Settings SET Value = N'x' WHERE TenantId = @tenant;
GO
DECLARE @tenant int = 1;
DELETE FROM dbo.Cache WHERE TenantId = @tenant;
```

or remove the `GO` if nothing in between needs its own batch.

## Assumptions (version / edition)

Not version or edition dependent. Batches are tracked per file; a variable declared in another
file is never "in scope" and is not what this rule is about.
