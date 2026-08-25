# MSSQL-REV-002 — No automatic rollback statement could be generated

**Default severity:** Warning (DDL, per statement) · Info (DML, one finding per file) · **Category:** Reversibility

> **Opt-in.** This rule, the generated reverse script and the `Rollback:` summary line only run with
> `--rollback` (or `"rollback": true` in `.planizer.json`). Teams that fix forward never see it;
> the data-loss rule [MSSQL-REV-001](MSSQL-REV-001.md) is always on. See
> [ADR-0003](../adr/0003-rollback-analysis-opt-in.md).

## What it checks

Every state-changing statement is fed to the rollback script builder, which knows these inverse
pairs:

| Statement | Generated inverse |
|---|---|
| `ALTER TABLE … ADD` (column/constraint) | `DROP COLUMN` / `DROP CONSTRAINT` |
| `CREATE INDEX` | `DROP INDEX` |
| `CREATE TABLE`, `SELECT … INTO` | `DROP TABLE` |
| `CREATE VIEW`, `CREATE PROCEDURE`, `CREATE FUNCTION`, `CREATE TRIGGER` | `DROP VIEW` / `DROP PROCEDURE` / `DROP FUNCTION` / `DROP TRIGGER` |
| `CREATE OR ALTER` / `ALTER` of a procedure, view, function or trigger | a `-- … redeploy the previous … definition from source control` instruction line — the prior body is not derivable offline, but it lives in version control and no data is at stake, so this is not flagged |
| `EXEC sp_rename` | reverse `sp_rename` (bracketed `N'[T].[C]'` names included) |
| `ALTER TABLE … ENABLE TRIGGER` / `DISABLE TRIGGER` | the opposite toggle |

Generated inverses are collected — in reverse order — into the report summary's
`rollbackScript`; when every statement got one, `rollbackComplete` is `true`. A **DDL** statement
the builder could not reverse gets this finding at Warning. **DML** (`INSERT`, `UPDATE`, `DELETE`,
`MERGE`) almost never has a derivable inverse — it depends on previous row values — so instead of
one warning per statement the rule emits a single Info finding per file, anchored at the first
such statement: "N data-modification statements in this file have no automatic inverse (INSERT×a,
UPDATE×b, DELETE×c)". Statements carrying a `planizer:ignore MSSQL-REV-002` comment leave the
count (see [ADR-0001](../adr/0001-rev-002-dml-findings-aggregated-per-file.md)). The rule stays quiet where the warning would be
noise on top of a stronger signal: irreversible statements (MSSQL-REV-001 — no script can
restore that data) and dynamic SQL (MSSQL-DYN-001 — contents unknown).

That first exclusion mirrors MSSQL-REV-001 exactly, join analysis included. A `DELETE` whose FROM
clause holds a join that **cannot drop rows of the target** — an outer join with the target on its
preserved side, a `CROSS JOIN` or comma cross join, an `OUTER APPLY` on the left — is REV-001's
Critical, so this rule keeps quiet about it. A `DELETE` whose join **may or may not** bound it
(`INNER JOIN`, `CROSS APPLY`) is inconclusive: REV-001 stays out of it, and the statement simply
joins this rule's per-file DML summary like any other `DELETE`. See
[MSSQL-LOCK-009](MSSQL-LOCK-009.md) for the full three-state table.

## Why it matters

"How do we roll this back?" is a standard deployment-review question, and the honest answer is
per-statement. Planizer auto-generates the mechanical part; this finding marks exactly the
statements where a human has to write the rollback — typically data changes (`UPDATE`,
`INSERT`) whose inverse depends on the *previous* values, which only you know.

## Example

```sql
CREATE INDEX IX_Orders_Status ON dbo.Orders (Status);
UPDATE dbo.Orders SET Status = 1 WHERE Status = 0;
INSERT INTO dbo.OrderLog (Note) VALUES ('migrated');
```

The `CREATE INDEX` auto-reverses to `DROP INDEX` (visible in the report's rollback script); the
`UPDATE` and `INSERT` together produce one finding on the UPDATE's line: `Info MSSQL-REV-002
2 data-modification statements in this file have no automatic inverse (INSERT×1, UPDATE×1); the
rollback script is incomplete — write the rollback by hand.` The summary shows
`rollbackComplete: false`. A DDL statement without an inverse, e.g. `DROP INDEX IX_Orders_Status
ON dbo.Orders;`, reports per statement: `Warning MSSQL-REV-002 No automatic inverse exists for
`DROP INDEX IX_Orders_Status ON dbo.Orders;`; a manual rollback script is needed for it.`


DML against table variables and temp tables (`INSERT INTO @t`, `DELETE FROM #t`,
`SELECT … INTO #t`) moves no persistent data and needs no rollback entry.

## How to fix

Write the missing rollback statements and keep them next to the migration. For the example:
the UPDATE's inverse needs the original rows (`UPDATE … SET Status = 0 WHERE …` only works if
nothing else sets Status to 1), and the INSERT's inverse is a keyed DELETE. When the rollback
is genuinely impossible or handled elsewhere, suppress with a reason:

```sql
-- planizer:ignore MSSQL-REV-002 rollback handled by restoring dbo.OrderLog from staging
INSERT INTO dbo.OrderLog (Note) VALUES ('migrated');
```

Seed-data and cleanup directories already get only one Info line per file; to silence even
that, disable the rule for the directory via its `.planizer.json`:

```json
{ "rules": { "MSSQL-REV-002": { "enabled": false } } }
```

## Assumptions (version / edition)

Not version or edition dependent. `CREATE OR ALTER` and constraints SQL Server would name
itself are deliberately not reversed (the pre-existing object / generated name cannot be known
statically), so they also produce this finding.
