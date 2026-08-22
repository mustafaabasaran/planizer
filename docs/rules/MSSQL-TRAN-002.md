# MSSQL-TRAN-002 — Unbalanced BEGIN TRAN / COMMIT / ROLLBACK

**Default severity:** Critical · **Category:** Transaction & script hygiene

## What it checks

Transaction control that does not add up on the **main path** — the path the script takes when
nothing fails. Two shapes are reported:

- a `BEGIN TRAN` that is still open when the main path reaches the end of the file (anchored to
  the BEGIN). A `ROLLBACK` that lives only in a `CATCH` block is the *error* path and does not
  close the transaction on success;
- a `COMMIT` or `ROLLBACK` reached on the main path with no transaction open — **error 3902 /
  3903, "The COMMIT/ROLLBACK TRANSACTION request has no corresponding BEGIN TRANSACTION"**
  (anchored to the stray statement). A `COMMIT` / `ROLLBACK` under `IF @@TRANCOUNT > 0`,
  `IF XACT_STATE() <> 0` or `WHILE @@TRANCOUNT > 0` (the "clear stale transactions" opener) is
  never reported.

The walk understands the usual shapes: `TRY` bodies are entered, `CATCH` bodies are not; the two
branches of an `IF` are simulated separately and the script continues with the branch that
changed the transaction state (so `IF @flag = 1 BEGIN TRAN; … IF @flag = 1 COMMIT;` balances); a
branch that ends in `RETURN`, `THROW` or `GOTO` leaves the script and is not continued (old-style
`IF @@ERROR <> 0 BEGIN ROLLBACK; RETURN; END` is fine); a top-level `RETURN` / `THROW` ends the
main path, so a `label:` handler written after it and reached only by `GOTO` (`IF @@ERROR <> 0
GOTO ERR; … COMMIT; RETURN; ERR: ROLLBACK;`) is the error path, not a stray `ROLLBACK`; a `GOTO`
to a later label in the same block jumps to it; `WHILE` bodies are walked once; nested
`BEGIN TRAN`s each need their own `COMMIT`; `ROLLBACK` closes every open level; `ROLLBACK` to a
savepoint closes nothing. Batches (`GO`) do not reset the count — a transaction survives `GO`
(MSSQL-TRAN-003 reports that separately).

## Why it matters

A transaction left open at the end of a script does not fail anything — it just **keeps every
lock it took** (the Sch-M of each ALTER, the X locks of each UPDATE) and silently swallows every
later batch into itself. The connection is returned to the pool; the next script the runner sends
on it runs inside the forgotten transaction; when the connection is finally closed everything is
rolled back, including work that looked committed. The stray `COMMIT` case is the mirror image:
a script that fails on its last line with 3902 after having applied everything in autocommit.

## Example

```sql
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
UPDATE dbo.A SET C1 = 0 WHERE C1 IS NULL;
```

Reports on line 1: `Critical MSSQL-TRAN-002 The transaction opened at line 1 is never committed
or rolled back on the success path; it is still open when the script ends, so its locks are held
and every later batch runs inside it until the connection closes.`

```sql
BEGIN TRY
    BEGIN TRAN;
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH
```

Reports the same on the `BEGIN TRAN` — the `ROLLBACK` covers only the failure path. And
`ALTER TABLE dbo.A ADD C1 int NULL; COMMIT;` reports on the COMMIT: `Critical MSSQL-TRAN-002
COMMIT at line 2 has no open transaction to close on this path; SQL Server raises error 3902
("The COMMIT TRANSACTION request has no corresponding BEGIN TRANSACTION").`

## How to fix

Close the transaction on the success path and keep the `CATCH` for the error path:

```sql
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRAN;
    ALTER TABLE dbo.A ADD C1 int NULL;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
```

A `COMMIT` that may legitimately run without a transaction (a shared footer) is guarded with
`IF @@TRANCOUNT > 0 COMMIT;`.

## Assumptions (version / edition)

Not version or edition dependent. The analysis is per file and static: it does not know whether
the runner itself wraps each script in a transaction (EF Core's migration scripts and Flyway do
by default; DbUp only with `WithTransaction()` / `WithTransactionPerScript()`) — in which case a stray `COMMIT` inside the script commits the *runner's*
transaction early, which is a different but equally real problem.
