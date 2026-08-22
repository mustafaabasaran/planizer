# MSSQL-REV-005 — SET IDENTITY_INSERT ON is never turned OFF

**Default severity:** Warning · **Category:** Reversibility

## What it checks

`SET IDENTITY_INSERT <table> ON` with no matching `SET IDENTITY_INSERT <table> OFF` **for the
same table, later in the same file**. Table comparison is case- and quoting-insensitive
(`[dbo].[Orders]` matches `dbo.Orders`); an OFF for a *different* table does not count.

## Why it matters

Only **one table per session** can have IDENTITY_INSERT ON, and it stays ON until the session
ends. A migration that forgets the OFF poisons the rest of its own connection: the next
`SET IDENTITY_INSERT … ON` in the same session fails with error 8107, and ordinary inserts
into the table now require explicit identity values (error 545). With connection pooling the
session may live long past the migration, turning this into an intermittent production bug.

## Example

```sql
SET IDENTITY_INSERT dbo.Orders ON;
INSERT INTO dbo.Orders (Id, Number) VALUES (1, 'A-1');
```

Reports: `Warning MSSQL-REV-005 … SET IDENTITY_INSERT dbo.Orders ON has no matching OFF in
this script; only one table per session can have it ON, and it stays ON until the session
ends.` — with the fix `SET IDENTITY_INSERT dbo.Orders OFF;`.

An OFF for another table does not silence it; the balanced script is clean:

```sql
SET IDENTITY_INSERT dbo.Orders ON;
INSERT INTO dbo.Orders (Id, Number) VALUES (1, 'A-1');
SET IDENTITY_INSERT [dbo].[Orders] OFF;
```

## How to fix

Add the matching statement immediately after the identity inserts:

```sql
SET IDENTITY_INSERT dbo.Orders OFF;
```

## Assumptions (version / edition)

Not version or edition dependent. Matching is per file — an OFF in a different script does not
count, deliberately: migration files must be self-contained.
