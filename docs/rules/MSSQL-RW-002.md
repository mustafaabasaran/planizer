# MSSQL-RW-002 — Adding a NOT NULL column with a default may rewrite the entire table

**Default severity:** Critical (Info when metadata-only) · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ADD <column> NOT NULL DEFAULT <value>`. The verdict is **edition dependent**,
read from the behavior catalog:

| Default | Edition | Verdict |
|---|---|---|
| runtime constant (`0`, `'x'`, `CAST(0 AS bit)`, `GETDATE()`, `SYSDATETIME()`, `CURRENT_TIMESTAMP`, `SUSER_SNAME()`) | Enterprise / Azure | **Info** — metadata-only |
| runtime constant | Standard / Express | **Critical** — entire table rewritten |
| per-row (`NEWID()`, `NEWSEQUENTIALID()`) | any | **Critical** — rewrite everywhere |

A **runtime constant** is anything evaluated *once at the start of the statement, regardless
of determinism* — so statement-level functions such as `GETDATE()` keep the fast path even
though they are non-deterministic. Only functions evaluated **per row** break it. Unrecognized
functions are conservatively treated as non-constant (pending the Docker validation pass).

## Why it matters

This is the classic MSSQL edition trap. Since SQL Server 2012, Enterprise stores a
runtime-constant default as metadata and the ADD returns instantly on any table size. The same
statement on **Standard** physically writes the value into every existing row — a full rewrite
under a Sch-M lock, minutes of total blocking on a large table. A migration tested on a
Developer-edition box (= Enterprise behavior) hides this completely. A per-row default breaks
the fast path even on Enterprise, because every row needs its own value.

## Example

```sql
ALTER TABLE dbo.Orders ADD Status int NOT NULL DEFAULT 0;
```

With `--edition standard` (the default): `Critical MSSQL-RW-002 … Adding NOT NULL column
Status with a default to dbo.Orders rewrites the entire table on Standard edition.`
With `--edition enterprise`: Info, metadata-only — the same applies to
`DEFAULT GETDATE()`, a statement-level runtime constant. But on any edition:

```sql
ALTER TABLE dbo.Orders ADD RowGuid uniqueidentifier NOT NULL DEFAULT NEWID();
```

is Critical — the per-row default breaks the metadata-only fast path.

## How to fix

Run during low traffic, or use **expand/contract**:

```sql
ALTER TABLE dbo.Orders ADD Status int NULL;                 -- metadata-only
-- backfill in batches (UPDATE TOP (4000) … WHERE Status IS NULL)
ALTER TABLE dbo.Orders ALTER COLUMN Status int NOT NULL;    -- see MSSQL-RW-007
```

## Assumptions (version / edition)

The whole point of this rule: run it with the **production** `--edition`. `developer` maps to
Enterprise behavior — safe on the dev box does not mean safe in production. Catalog rows:
`add_column_notnull_default_const` (per edition), `add_column_notnull_default_nondet` (any).
