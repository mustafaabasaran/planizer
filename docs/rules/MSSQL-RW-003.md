# MSSQL-RW-003 — Adding a NOT NULL column without a default fails on a non-empty table

**Default severity:** Blocker · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ADD <column> NOT NULL` with **no DEFAULT**. SQL Server rejects this with error
4901 the moment the table contains a single row — there is no value to put into the existing
rows' new NOT NULL column.

## Why it matters

Offline the row count is unknown, but the asymmetry decides the severity: on an empty table
the statement is pointless-but-harmless, on a populated **production** table it fails outright
and aborts the migration mid-run. A statement that cannot succeed against realistic production
data is a Blocker — it should never reach the deployment pipeline in this form.

## Example

```sql
ALTER TABLE dbo.Orders ADD Code int NOT NULL;
```

Reports: `Blocker MSSQL-RW-003 … Adding NOT NULL column Code without a default fails if
dbo.Orders has rows.`

Both safe variants are clean: `ADD Code int NOT NULL DEFAULT 0` (that one is judged by
MSSQL-RW-002) and `ADD Memo nvarchar(100) NULL` (MSSQL-RW-001).

## How to fix

Either declare a default on the ADD:

```sql
ALTER TABLE dbo.Orders ADD Code int NOT NULL DEFAULT 0;   -- edition caveats: MSSQL-RW-002
```

or use expand/contract: add the column as nullable, backfill existing rows in batches, then
`ALTER TABLE dbo.Orders ALTER COLUMN Code int NOT NULL;` (MSSQL-RW-007).

## Assumptions (version / edition)

Fails on every version and edition (catalog row `add_column_notnull_no_default`,
`fails_if_rows`). Offline mode cannot see the row count; the message says "fails **if** the
table has rows" on purpose.
