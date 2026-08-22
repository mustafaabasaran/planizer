# MSSQL-TRAN-005 — CATCH block swallows the error

**Default severity:** Warning · **Category:** Transaction & script hygiene

## What it checks

A `TRY…CATCH` whose `CATCH` body does not rethrow or fail the batch. A rethrow is any of:

- `THROW;` (or `THROW 50000, …`), at any depth inside the `CATCH` — `IF ERROR_NUMBER() <> 2705
  BEGIN THROW; END` counts;
- `RAISERROR` with a **literal severity of 11 or higher**, or with a non-literal severity —
  the pre-2012 idiom that passes `ERROR_SEVERITY()` through a variable is assumed to rethrow;
- `EXEC` of a procedure whose name contains `throw` or `raise` (`dbo.usp_RethrowError`).

An empty `CATCH`, a `CATCH` that only `PRINT`s or logs, and a `RAISERROR` with severity 0–10 (an
informational message that does not fail the batch) are reported. Anchored to the `BEGIN TRY`.

## Why it matters

A `CATCH` that swallows the error turns a failed migration into a **successful** one as far as
anyone can tell: the batch ends normally, the migration runner records the script as applied, the
pipeline goes green, and the schema is left half-changed — a column added but not backfilled, an
index missing, a constraint never created. It is found later, by the application, and by then
the runner will refuse to re-apply a script it considers done. `PRINT ERROR_MESSAGE()` in a
pipeline log nobody reads is not error handling.

## Example

```sql
BEGIN TRY
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    PRINT ERROR_MESSAGE();
END CATCH
```

Reports on line 1: `Warning MSSQL-TRAN-005 The CATCH block of the TRY starting at line 1 neither
rethrows nor fails the batch; an error inside the TRY is swallowed, the script reports success,
and the migration runner marks a half-applied script as done.` An empty `CATCH` reports `… is
empty; …`, and `RAISERROR('migration step failed', 10, 1)` reports `… only raises an
informational message (RAISERROR severity below 11 does not fail the batch); …`. Rolling back in
the `CATCH` and then logging to a table instead of rethrowing is reported too — the rollback is
right, the silence is not.

Quiet:

```sql
BEGIN CATCH
    DECLARE @msg nvarchar(4000) = ERROR_MESSAGE(), @sev int = ERROR_SEVERITY(), @state int = ERROR_STATE();
    RAISERROR(@msg, @sev, @state);
END CATCH
```

## How to fix

End the `CATCH` with a rethrow:

```sql
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
```

On SQL Server 2008/2008 R2 (no `THROW`), use `RAISERROR(@msg, 16, 1)` with the message captured
from `ERROR_MESSAGE()`. If a specific error is genuinely expected and safe to ignore, test for it
and rethrow everything else: `IF ERROR_NUMBER() <> 2705 THROW;`.

## Assumptions (version / edition)

Not version or edition dependent. The rule looks at the `CATCH` body only; it does not know
whether the migration runner treats a non-zero `@@ERROR` or an informational message as failure
(none of the common ones do).
