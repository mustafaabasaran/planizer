# MSSQL-LIT-001 — Non-ASCII string literal without the N prefix

**Default severity:** Warning · one finding per file · **Category:** Failure risk

## What it checks

String literals written **without the `N` prefix** (`'Ödeme Türü'` rather than `N'Ödeme Türü'`)
that contain at least one character outside ASCII (code point > 127). The rule walks every
deploy-time statement that can **store** the literal — DML, `EXEC` arguments, `IF` predicates,
`SELECT … INTO` — but not module bodies (`CREATE PROCEDURE` / `FUNCTION` / `TRIGGER` / `VIEW`,
which are definitions, not migration actions) and not **message text**: the arguments of
`PRINT`, `RAISERROR` and `THROW` are shown in the deployment log and never reach a column, so a
`?` there is cosmetic, not a data risk.

Like MSSQL-REV-002's DML summary ([ADR-0001](../adr/0001-rev-002-dml-findings-aggregated-per-file.md)),
the rule reports **once per file**: the finding is anchored at the first offending statement and
carries the count plus the first examples. Statements carrying `-- planizer:ignore MSSQL-LIT-001`
leave the count (and the anchor moves to the next one).

## Why it matters

Without `N` the literal is **varchar in the database's default collation**. Characters that the
collation's code page cannot represent are replaced by `?` at that moment — before the value
reaches the `nvarchar` column it is being written to, so the column type does not save you. A
seed script developed against a Turkish-collated database runs "fine" there and silently stores
`?deme T?r?` on a customer's `SQL_Latin1_General_CP1_CI_AS` server. Nothing fails; the data is
just wrong, and it is found weeks later on a screen.

## Example

```sql
INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CASH', 'Nakit Ödeme');
INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CARD', 'Kredi Kartı');
PRINT 'Ödeme türleri yüklendi';
```

Reports once, on line 1: `Warning MSSQL-LIT-001 2 string literals in this file contain non-ASCII
characters without the N prefix (first: 'Nakit Ödeme' at line 1; also 'Kredi Kartı'). Without N
the literal is varchar in the database's default collation: characters outside that collation's
code page are replaced by '?' before the value ever reaches an nvarchar column.`

`'CASH'` and `'CARD'` are ASCII-only and safe in any code page; `N'Açıklama'` is already Unicode;
the `PRINT` on line 3 is message text and is not counted — a file containing only such `PRINT` /
`RAISERROR` literals produces no finding.

## How to fix

Prefix the literals with `N`:

```sql
INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CASH', N'Nakit Ödeme');
```

When the target column really is `varchar` and the database collation is known to cover the
characters, suppress with a reason:

```sql
-- planizer:ignore MSSQL-LIT-001 column is varchar, Turkish_CI_AS database
INSERT INTO dbo.Branch (Name) VALUES ('Şube 1');
```

## Assumptions (version / edition)

Not version or edition dependent. The rule cannot see the database collation offline; it flags
every literal that *could* lose characters on a differently collated server.
