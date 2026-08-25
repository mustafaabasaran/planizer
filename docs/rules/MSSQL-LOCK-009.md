# MSSQL-LOCK-009 — Unbounded UPDATE/DELETE escalates to a table lock

**Default severity:** Warning (Info when a join's effect is undecidable) · **Category:** Locking

## What it checks

`UPDATE` or `DELETE` statements with **no WHERE clause and no TOP** — they touch every row of
the table. A `JOIN` in the FROM clause is examined rather than assumed to be a filter: see
*What counts as a filter* below.

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

## What counts as a filter

A `JOIN` in the FROM clause bounds the write only when it can actually **drop rows of the
target**. The rule classifies each statement into one of three states, based on where the target
sits in the join tree:

| Shape | State | Reported |
|---|---|---|
| Target on the **null-supplying** side of a `LEFT`/`RIGHT` outer join — filtered exactly like an inner join, so whether every row matches is a data question | **inconclusive** | Info, `inconclusive: true` |
| Target on the **preserved** side of a `LEFT`/`RIGHT` outer join, or either side of `FULL OUTER JOIN` | unbounded | Warning |
| `CROSS JOIN`, or the comma cross join (`FROM dbo.A a, dbo.B b`) | unbounded | Warning |
| `OUTER APPLY` with the target on the left | unbounded | Warning |
| Target not in the FROM clause at all (`UPDATE dbo.A … FROM dbo.B JOIN dbo.C`) — T-SQL cross joins it in | unbounded | Warning |
| `INNER JOIN`, `CROSS APPLY` | **inconclusive** | Info, `inconclusive: true` |

In a comma-separated FROM list (`FROM dbo.X x, dbo.A a LEFT JOIN dbo.B t ON …`) the verdict of the
reference that holds the target stands — the other references cross join against it and can
multiply rows but never resurrect the ones the target's own joins dropped. Only a **bare** target
in a multi-reference list is decided by the comma cross join itself.

`DELETE t FROM dbo.Orders t LEFT JOIN dbo.Customers c ON c.Id = t.CustomerId;` deletes **every**
row of `dbo.Orders` — the outer join preserves the left side in full — and reports
`Warning MSSQL-LOCK-009 DELETE on dbo.Orders has no WHERE and no TOP, and the LEFT JOIN does not
restrict dbo.Orders: …`.

An `INNER JOIN` is the undecidable case: it drops target rows without a match, but the ON
predicate may match every row. Offline there is no way to tell, and per the project's rule that a
rule never stays silent it reports Info instead:
`Info MSSQL-LOCK-009 DELETE on dbo.ParameterGroupTranslation has no WHERE and no TOP; how many
rows it touches depends on the cardinality of the INNER JOIN, which may match every row — then
~5000 row locks escalate into a table lock. A schema snapshot settles this. [inconclusive]`.
A schema/statistics snapshot (Phase 2) turns this into a decided verdict. `CROSS APPLY` is the
same case: it drops the left row only if its subquery can come back empty.
[MSSQL-REV-001](MSSQL-REV-001.md) deliberately does *not* mirror the inconclusive state — a
Critical data-loss finding on a guess would be wrong.

When the join path holds several joins, the **strongest** restriction on the way from the FROM
clause down to the target wins: one filtering join is enough to keep the write off the rest of
the table.

Table variables (`@t`) and temp tables (`#t`) are ignored throughout — they are session-scoped and
escalate no locks on user tables. An aliased target (`UPDATE T SET … FROM dbo.Big T`) is resolved
through the FROM clause: the finding names `dbo.Big`, while `DELETE T FROM #tmp T` and
`DELETE i FROM @Ids i CROSS JOIN …` count as transient writes.

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
