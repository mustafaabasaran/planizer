# MSSQL-RW-005 — Column type change may rewrite the table depending on the current type

**Default severity:** Critical (reported Warning + inconclusive offline) · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ALTER COLUMN` that respecifies a type and carries **no other certain signal** —
no MAX (MSSQL-RW-004), no NOT NULL/NULL clause (MSSQL-RW-007/008), no COLLATE (MSSQL-RW-009).

Offline the *current* type is unknown, so widening (metadata-only), fixed-length change
(rewrite) and narrowing (data loss) cannot be told apart. Per Planizer's core contract the rule
does not stay silent — it reports **Warning + inconclusive**.

## Why it matters

The same syntax hides three very different operations. `varchar(50)→varchar(100)` is free;
`int→bigint` (or any fixed-length/precision/scale change) rewrites every row of the table
under a Sch-M lock; `varchar(100)→varchar(50)` adds data loss on top. Which one this statement
is depends entirely on the type the column has *today* — information the script does not
contain. An honest report says exactly that, instead of guessing or going quiet.

## Example

```sql
ALTER TABLE dbo.Orders ALTER COLUMN Id bigint;
```

Reports: `Warning MSSQL-RW-005 … Column Id of dbo.Orders changes type to bigint; whether this
is a rewrite depends on the current type — verify. [inconclusive]`

Statements with a certain signal are routed to their own rule and do not appear here:
`nvarchar(MAX)` → RW-004, `bigint NOT NULL` → RW-007.

## How to fix

Check the current type. If the storage differs (e.g. `int→bigint`, precision/scale change),
use expand/contract:

```sql
ALTER TABLE dbo.Orders ADD Id_new bigint NULL;
-- backfill in batches, keep in sync (trigger or dual-write), then swap names
```

If it is a plain in-family widen, the statement is metadata-only and can be suppressed with a
reason.

## Assumptions (version / edition)

Not edition dependent. Offline mode cannot compare against the current type — with a schema
snapshot (Phase 2) this becomes the conclusive fixed-length-change rewrite rule (Critical, catalog
row `alter_column_fixed_len_change`).
