# MSSQL-DYN-001 — Dynamic SQL cannot be analyzed statically

**Default severity:** Warning · **Category:** Parse & dynamic SQL

## What it checks

Every statement that executes a string or a variable instead of inline SQL:

- `EXEC ('…')` — executing a string literal or expression,
- `EXEC @sql` — executing the contents of a variable,
- `EXEC sp_executesql N'…'` — the parameterized variant.

These statements are classified as *Dynamic* and also feed the report summary's
`unanalyzableCount`.

## Why it matters

Dynamic SQL is a blind spot for every static rule: the executed string can contain any DDL or
DML — a `DROP TABLE`, an unbounded `DELETE` — without a single other rule firing. A report that
stayed silent here would look clean while guaranteeing nothing. Planizer flags each dynamic
statement so a human reviews what the string actually does.

## Example

```sql
EXEC ('DROP TABLE dbo.Unknown');
EXEC sp_executesql N'UPDATE dbo.T SET C = 1';
EXEC @procName;
```

Each line reports: `Warning MSSQL-DYN-001 … Dynamic SQL cannot be analyzed statically; review
manually.`

A static procedure call (`EXEC dbo.RebuildAllIndexes;`) is *not* dynamic SQL and does not
trigger this rule.

## How to fix

Prefer inlining the SQL so it can be analyzed. Where dynamic SQL is genuinely needed (e.g.
object names decided at runtime), review the generated statements manually and suppress the
finding with a reason:

```sql
-- planizer:ignore MSSQL-DYN-001 reviewed: only rebuilds indexes listed in dbo.MaintenanceList
EXEC @sql;
```

## Assumptions (version / edition)

Not version or edition dependent.
