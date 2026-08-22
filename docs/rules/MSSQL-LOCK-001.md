# MSSQL-LOCK-001 — Schema modification lock (Sch-M) blocks all access to the table

**Default severity:** Warning · **Category:** Locking

## What it checks

Every statement that acquires a schema-modification (Sch-M) lock:

- `ALTER TABLE` in all its variants (`SWITCH` included),
- `DROP TABLE`,
- `TRUNCATE TABLE`,
- `EXEC sp_rename …`.

The behavior catalog decides how loud the finding is: a metadata-only operation holds Sch-M
only briefly and reports as **Info** ("brief Sch-M"); everything else holds it for the duration
of the operation and reports as **Warning**. When the catalog has no row for the operation, the
rule does not stay silent — it reports an inconclusive Warning.

Metadata-only operations reported as Info include adding a default for an existing column
(`ADD [CONSTRAINT DF_x] DEFAULT 0 FOR C` — existing rows are not touched) and
`ENABLE TRIGGER` / `DISABLE TRIGGER`.

## Why it matters

A Sch-M lock is exclusive against *everything*: while it is held, no session can read or write
the table — not even `SELECT`s under `NOLOCK`. On a busy table, a Sch-M held for the duration
of a long rewrite is an outage. Knowing which statements take Sch-M, and for how long, is the
first question of any migration review.

## Example

```sql
ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT 0;
DROP TABLE dbo.Old;
```

They report `Warning MSSQL-LOCK-001 ADD COLUMN C takes a schema-modification (Sch-M) lock on
dbo.T, held for the duration of the operation; all reads and writes are blocked.` and
`Warning MSSQL-LOCK-001 DROP TABLE takes a schema-modification (Sch-M) lock on dbo.Old, …` — the
message names the operation (and the column or constraint it touches) so several findings on the
same table read differently.

A metadata-only operation is quieter:

```sql
EXEC sp_rename 'dbo.OldName', 'dbo.NewName';
```

reports `Info MSSQL-LOCK-001 sp_rename takes a brief schema-modification (Sch-M) lock on
dbo.OldName; metadata-only change, blocking is momentary.`


DDL on temp tables (`#t`, `##t`) is ignored: they are session-scoped, so their Sch-M lock blocks
no one else.

## How to fix

This rule is descriptive: it maps the blocking surface of the script. Reduce it by preferring
metadata-only forms (see the MSSQL-RW family), keeping Sch-M statements out of long
transactions (MSSQL-LOCK-007), and running unavoidable duration-length Sch-M operations in a
low-traffic window.

## Assumptions (version / edition)

Whether an operation is metadata-only (→ Info) or not (→ Warning) is read from the behavior
catalog and can depend on `--target-version` and `--edition` — e.g. `ADD COLUMN NOT NULL
DEFAULT const` is metadata-only on Enterprise but a rewrite on Standard/Express.
