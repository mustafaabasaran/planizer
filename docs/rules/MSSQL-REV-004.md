# MSSQL-REV-004 — TRUNCATE TABLE: rollback window and FK restrictions

**Default severity:** Warning · **Category:** Reversibility

## What it checks

Every `TRUNCATE TABLE`. The message adapts to context:

- inside an explicit transaction — "can be rolled back until the enclosing transaction commits",
- outside one — "is not inside an explicit transaction, so there is no rollback window".

Either way the statement fails outright if the table is referenced by a foreign key — which a
script alone cannot reveal, so the finding is always marked **inconclusive** offline.

## Why it matters

Two widespread misconceptions meet in TRUNCATE. First, "TRUNCATE cannot be rolled back" —
false: inside a transaction it deallocates pages under the transaction and rolls back fine;
*outside* one there is no window at all, and after commit the data is gone either way (that
part is MSSQL-REV-001's Critical). Second, TRUNCATE fails with error 4712 when **any** foreign
key references the table — even from an empty table — a fact the script cannot show, so a
migration that passed on a bare dev database can fail in production.

## Example

```sql
TRUNCATE TABLE dbo.Staging;
```

Reports: `Warning MSSQL-REV-004 … TRUNCATE TABLE is not inside an explicit transaction, so
there is no rollback window; it fails if dbo.Staging is referenced by a foreign key, which
cannot be verified offline. [inconclusive]`

Wrapped in `BEGIN TRAN … COMMIT` the first half of the message changes accordingly; a batched
`DELETE … WHERE` does not trigger the rule.


`TRUNCATE TABLE #t` on a temp table is ignored (session-scoped, no FK can reference it).

## How to fix

If the table may be FK-referenced or a rollback window is needed, delete in batches instead:

```sql
WHILE 1 = 1
BEGIN
    DELETE TOP (4000) FROM dbo.Staging WHERE <predicate>;
    IF @@ROWCOUNT = 0 BREAK;
END
```

Otherwise wrap the TRUNCATE in the migration's transaction so it stays reversible until commit.

## Assumptions (version / edition)

Not version or edition dependent. The FK check needs schema data — with a snapshot (Phase 2) the
finding becomes conclusive.
