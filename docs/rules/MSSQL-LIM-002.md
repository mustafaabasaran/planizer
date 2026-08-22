# MSSQL-LIM-002 — Identifier longer than SQL Server allows

**Default severity:** Blocker · **Category:** Failure risk

## What it checks

Names that SQL Server refuses at execution time although the T-SQL grammar accepts them:

- **local temporary table names longer than 116 characters**, the `#` included — the server needs
  the remaining 12 characters of the 128-character identifier for the suffix that makes the name
  unique per session. Global temporary tables (`##`) carry no suffix and may use the full 128;
- **variable names longer than 128 characters**, the `@` included.

Regular identifiers over 128 characters never get this far: ScriptDom rejects them while parsing
(error 46095), which MSSQL-PARSE-001 reports. Module bodies are scanned too, because a too-long
name inside a procedure fails when the procedure is *created*, not when it is first called. Each
distinct name is reported once per statement.

## Why it matters

**Error 103, "The identifier that starts with '#TTTT…' is too long. Maximum length is 116."** —
at run time, from a statement that looked perfectly valid to every editor and linter, typically a
generated name: a tool-built temp table whose name concatenates a prefix, a schema and a GUID, or
a variable named after a long column. The batch fails; in a migration that means the preceding
batches are applied and this one is not.

## Example

```sql
CREATE TABLE #TTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT (Id int NOT NULL);
```

(117 characters including `#`.) Reports: `Blocker MSSQL-LIM-002 Temporary table name
'#TTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT…' is 117 characters long; SQL Server allows at most 116
(error 103 at execution).` A 129-character `DECLARE @V…` reports `Variable name '@VVV…' is 129
characters long; SQL Server allows at most 128 (error 103 at execution).` — once for the DECLARE
and once more for each statement that uses it.

Exactly 116 / 128 characters is accepted and stays quiet — and so is a 120-character `##Global…`
name, because the 116 cap applies to local temp tables only.

## How to fix

Shorten the name:

```sql
CREATE TABLE #OrdersToArchive (Id int NOT NULL);
```

## Assumptions (version / edition)

Not version or edition dependent — the limits are the same on every SQL Server, Azure SQL
included.
