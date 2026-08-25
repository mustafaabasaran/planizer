# MSSQL-LOCK-002 — Offline index build blocks access to the table

**Default severity:** Warning · **Category:** Locking

## What it checks

`CREATE INDEX` without `ONLINE = ON`:

- a **nonclustered** build holds a shared (S) table lock — writes are blocked during the build;
- a **clustered** build holds a Sch-M lock — *all* access is blocked.

## Why it matters

An index build takes as long as the table is big. Offline, that whole time is a blocking
window: on a nonclustered build every `INSERT`/`UPDATE`/`DELETE` waits; on a clustered build
even reads wait. On a large production table this is the classic "the deploy froze the app"
migration.

## Example

```sql
CREATE INDEX IX_T_C ON dbo.T (C);
CREATE CLUSTERED INDEX CX_T ON dbo.T (C);
```

Reports (Standard edition, the default assumption):

- `Warning MSSQL-LOCK-002 … Offline nonclustered index build takes a shared (S) lock on dbo.T:
  writes blocked during build.`
- `Warning MSSQL-LOCK-002 … Offline clustered index build takes a schema-modification (Sch-M)
  lock on dbo.T: all access blocked until the build completes.`

## How to fix

The suggested fix depends on the edition assumption:

- **Enterprise / Azure:** build the index online — `CREATE INDEX … WITH (ONLINE = ON);`
  (also see MSSQL-LOCK-004 and MSSQL-LOCK-005 for `WAIT_AT_LOW_PRIORITY` and `RESUMABLE`).
- **Standard / Express:** `ONLINE` is not available (see MSSQL-LOCK-003); schedule a
  maintenance window instead.

## Assumptions (version / edition)

The finding itself fires on every edition; only the fix text changes. Online index build is an
Enterprise (and Azure) feature.

## Sources

If you arrive here from the `CREATE INDEX` reference page you may find its `ONLINE = OFF`
wording confusing, so the claim is worth defending explicitly: **an offline nonclustered build
takes a table-level shared (S) lock for the duration — reads are allowed, writes are blocked.**

- *ALTER TABLE index_option* (`ONLINE = OFF`) states it verbatim: table locks are applied for the
  duration of the index operation, and creating a nonclustered index acquires a shared (S) lock
  on the table — which prevents updates to the underlying table but allows read operations such
  as `SELECT`.
- The **lock compatibility matrix** settles it independently: a table-level S lock is
  incompatible with the intent-exclusive (IX) lock a writer must take on the table, so no
  `INSERT`/`UPDATE`/`DELETE` can proceed while it is held; it *is* compatible with the IS lock a
  reader takes.
- The `CREATE INDEX` / `ALTER INDEX` pages were reworded in a **February 2025 docs refresh** into
  a sentence that contradicts both its own surrounding paragraph and the sibling `ALTER TABLE`
  page. Planizer follows the `ALTER TABLE` page and the compatibility matrix. Settling this
  empirically against a real instance (does an `INSERT` succeed during an offline nonclustered
  `CREATE INDEX`?) is a tracked item in `docs/ROADMAP.md`.

A **clustered** build is not in dispute: it takes Sch-M and blocks all access.
