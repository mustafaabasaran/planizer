# MSSQL-PARSE-001 — SQL script does not parse

**Default severity:** Blocker · **Category:** Parse & dynamic SQL

## What it checks

Every input file is parsed with Microsoft's T-SQL parser (ScriptDom), using the grammar of the
configured `--target-version`. When that grammar rejects the file, the analyzer re-parses it with
each **newer** grammar ScriptDom ships (2016 → 2017 → 2019 → 2022 → 2025 → post-2025 preview):

- if one of them accepts the file, the error is **not** a parse error but newer syntax —
  reported as [MSSQL-VER-001](MSSQL-VER-001.md) (Blocker), and analysis continues with that
  newer parse so every other rule still runs;
- if none does, each remaining error becomes a `MSSQL-PARSE-001` finding with the message
  `Parse error: {ScriptDom message}` at the error's exact line and column.

PARSE-001 is therefore reserved for SQL that no SQL Server grammar accepts.

Unlike the other rules, this finding is produced by the analyzer itself, not by a rule class —
a script that does not parse cannot be analyzed by any other rule.

## Why it matters

A migration that does not parse will fail the moment it reaches the server, and everything the
other rules would have told you about it is unknown. A parse error is therefore always a
Blocker: the report cannot vouch for a script it could not read.

Syntax introduced after your `--target-version` (for example `DROP TABLE IF EXISTS` before 2016,
or `TRIM(LEADING … FROM …)` before 2022) is *not* this rule: it parses with a newer grammar and
is reported as MSSQL-VER-001 with the version that first accepts it.

One parse error that surprises people is **error 46010, "Incorrect syntax near 'CREATE'"**: a
`CREATE PROCEDURE` / `VIEW` / `FUNCTION` / `TRIGGER` / `SCHEMA` that is not the **first statement
of its batch** (`SELECT 1; CREATE PROCEDURE dbo.P AS …` without a `GO` in between). SQL Server
requires these to be alone in their batch, and ScriptDom enforces it at parse time, so there is
no separate rule for it (the planned MSSQL-BATCH-003) — this finding is it.

## Example

```sql
ALTER TABLE dbo.Orders ADD;   -- incomplete statement
```

Reported: `Blocker MSSQL-PARSE-001 … Parse error: Incorrect syntax near ;.` — and the process
exits with code 1.

## How to fix

Fix the syntax error; for a module definition that shares its batch, put `GO` before the
`CREATE`. Newer-than-target syntax is a MSSQL-VER-001 finding and is fixed by raising
`--target-version` or rewriting the statement.

## Assumptions (version / edition)

The first parse follows `--target-version` (2014 → TSql120, 2016 → TSql130, 2017 → TSql140,
2019 → TSql150, 2022/azure → TSql160); the re-parse ladder continues through TSql170 (2025) and
TSql180 (preview). Edition does not affect parsing.
