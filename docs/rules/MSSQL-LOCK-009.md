# MSSQL-LOCK-009 — Unbounded UPDATE/DELETE escalates to a table lock

**Default severity:** Warning · **Category:** Locking

## What it checks

`UPDATE` or `DELETE` statements with **no WHERE clause and no TOP** — they touch every row of
the table.

## Why it matters

SQL Server starts with row locks, but once a single statement accumulates roughly **5000
locks**, lock escalation converts them into one table lock. From that moment the whole table is
blocked for everyone until the statement (and its transaction) completes. On a big table an
unbounded UPDATE/DELETE also produces one giant log-heavy transaction whose rollback takes as
long as the operation itself. (An unbounded DELETE is additionally irreversible — see
MSSQL-REV-001.)

## Example

```sql
DELETE FROM dbo.Big;
UPDATE dbo.Big SET Archived = 1;
```

Both report: `Warning MSSQL-LOCK-009 … has no WHERE and no TOP: it touches every row, and
after ~5000 row locks lock escalation turns it into a table lock.`

Bounded statements are fine: `DELETE FROM dbo.Big WHERE Id < 100;` or
`DELETE TOP (4000) FROM dbo.Big;` do not trigger the rule.


Table variables (`@t`) and temp tables (`#t`) are ignored — they are session-scoped and escalate no
locks on user tables — and a DELETE/UPDATE whose FROM clause joins other tables counts as bounded
by the join even without a WHERE. An aliased target (`UPDATE T SET … FROM dbo.Big T`) is resolved
through the FROM clause: the finding names `dbo.Big`, and `DELETE T FROM #tmp T` counts as a temp
table write.

## How to fix

Batch the operation so each statement stays under the escalation threshold and commits its
locks frequently:

```sql
WHILE 1 = 1
BEGIN
    DELETE TOP (4000) FROM dbo.Big WHERE <condition>;
    IF @@ROWCOUNT = 0 BREAK;
END
```

(The equivalent `UPDATE TOP (4000) … WHERE <not-yet-updated condition>` template applies to
updates — the WHERE must exclude already-updated rows or the loop never ends.)

## Assumptions (version / edition)

Not version or edition dependent. The ~5000-lock escalation threshold is a server default.
