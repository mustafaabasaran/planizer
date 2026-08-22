# MSSQL-RW-015 — Adding a PRIMARY KEY/UNIQUE constraint builds an index with a uniqueness scan

**Default severity:** Warning · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ADD CONSTRAINT PRIMARY KEY` or `UNIQUE` on an **existing** table. The message
names the index kind the constraint implies:

- PRIMARY KEY defaults to **clustered** → the build holds a Sch-M lock: all access blocked
  (and on a heap it is a full table rewrite — MSSQL-RW-013 territory),
- UNIQUE defaults to **nonclustered** → an offline build holds an S lock: writes blocked,
- an explicit `CLUSTERED`/`NONCLUSTERED` keyword overrides the default.

A PK declared inside `CREATE TABLE` builds its index on an empty table and is not flagged.

## Why it matters

An `ADD CONSTRAINT` line does not look like an index build, but that is exactly what it is —
plus a **uniqueness validation scan** over all existing data, with the offline index locking
profile held for the duration. And a single duplicate fails the statement after the whole
scan. The clustered-PK-on-a-heap case is the expensive double: uniqueness scan *and* full
table rewrite in one innocent statement.

## Example

```sql
ALTER TABLE dbo.Orders ADD CONSTRAINT PK_Orders PRIMARY KEY (Id);
ALTER TABLE dbo.Orders ADD CONSTRAINT UQ_Orders_Code UNIQUE (Code);
```

Line 1 reports: `… builds a clustered index on dbo.Orders and scans it to validate
uniqueness; a clustered index build holds a Sch-M lock on the table: all access is blocked for
the duration of the build.` Line 2 the nonclustered/S-lock variant.

The clean fixture: the same PK inside `CREATE TABLE dbo.NewTable (…)` is not flagged.

## How to fix

Prove uniqueness cheaply before paying for the scan
(`SELECT Code FROM dbo.Orders GROUP BY Code HAVING COUNT(*) > 1;` must return nothing).
Schedule the build; on Enterprise consider creating a unique index `WITH (ONLINE = ON)` first
and adding the constraint against it. If the table has a natural clustered index already,
declare the PK `NONCLUSTERED` to avoid the rewrite.

## Assumptions (version / edition)

Not version or edition dependent (catalog row `add_pk_or_unique`); the fix options (ONLINE)
are Enterprise/Azure features (MSSQL-LOCK-002/003).
