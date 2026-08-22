# MSSQL-TRAN-004 — BEGIN TRAN inside TRY without ROLLBACK in CATCH

**Default severity:** Critical · **Category:** Transaction & script hygiene

## What it checks

A `BEGIN TRAN` anywhere inside a `TRY` body — at the top level of the `TRY` or nested in an `IF` /
`BEGIN…END` / `WHILE` within it — whose enclosing `CATCH` block contains no `ROLLBACK`. Nested
`TRY…CATCH` is honoured: an inner `CATCH` that only rethrows hands the error to the next `CATCH`
outward, so a `ROLLBACK` in **any** enclosing `CATCH` satisfies the rule. The `ROLLBACK` may itself
be nested (`IF XACT_STATE() <> 0 BEGIN ROLLBACK TRANSACTION; END`). `ROLLBACK TRANSACTION
<savepoint>` — a name declared with `SAVE TRANSACTION` anywhere in the file — does **not**
satisfy the rule: it undoes work back to the savepoint and leaves the transaction open, and when
the error has doomed the transaction (`XACT_STATE() = -1`) the savepoint rollback itself fails
with error 3931; the finding then says so and names the savepoint. Anchored to the `BEGIN TRAN`.

## Why it matters

When a statement in the `TRY` fails, control jumps to the `CATCH` with the transaction **still
open** — or, under `SET XACT_ABORT ON` or after a severe error, *doomed* (uncommittable). A `CATCH`
that does not roll back then either leaves the open transaction behind when the batch ends
(locks held until the connection closes, every later batch inside it) or trips over the doomed
one: **error 3998, "Uncommittable transaction is detected at the end of the batch. The
transaction is rolled back"**, or **3930** on the first statement in the `CATCH` that tries to
write — which is typically the logging `INSERT` meant to record the failure.

## Example

```sql
BEGIN TRY
    BEGIN TRAN;
    ALTER TABLE dbo.A ADD C1 int NULL;
    COMMIT;
END TRY
BEGIN CATCH
    THROW;
END CATCH
```

Reports on line 2: `Critical MSSQL-TRAN-004 The transaction opened at line 2 inside the TRY
block starting at line 1 is not rolled back in its CATCH block; after an error it is left open or
uncommittable (error 3998), blocking everything behind its locks.`

Quiet — the inner `CATCH` only rethrows, the outer one owns the rollback:

```sql
BEGIN TRY
    BEGIN TRAN;
    BEGIN TRY
        ALTER TABLE dbo.A ADD C1 int NULL;
    END TRY
    BEGIN CATCH
        PRINT 'inner';
        THROW;
    END CATCH
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH
```

## How to fix

Make the rollback the first thing the `CATCH` does, guarded so it also works when the error
happened before `BEGIN TRAN` or after `COMMIT`:

```sql
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
```

Keep the `THROW` (MSSQL-TRAN-005): rolling back without rethrowing makes the runner believe the
script succeeded.

## Assumptions (version / edition)

Not version or edition dependent. A `BEGIN TRAN` outside any `TRY` is not this rule's concern
(MSSQL-TRAN-001 / MSSQL-TRAN-002 cover it).
