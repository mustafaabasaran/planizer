# MSSQL-RW-014 — Adding a CHECK/FOREIGN KEY constraint scans all existing rows

**Default severity:** Warning (Info for the WITH NOCHECK variant) · **Category:** Rewrite vs metadata-only

## What it checks

`ALTER TABLE … ADD CONSTRAINT` of a `CHECK` or `FOREIGN KEY` constraint. Both variants are
reported:

- default (`WITH CHECK` is implied for newly added constraints) — **Warning**: every existing
  row is validated under a Sch-M lock,
- `WITH NOCHECK` — **Info**: the scan is skipped, but the constraint is **untrusted** and the
  optimizer will not use it.

## Why it matters

The validation scan runs with all access to the table blocked — on a large table, minutes of
outage for one constraint, and a single violating row fails the statement *after* the scan.
`WITH NOCHECK` avoids that, but the trade-off is invisible and permanent until fixed: an
untrusted constraint still enforces new writes, yet the optimizer ignores it for plan choices
(join elimination, contradiction detection), so queries can silently get slower plans forever.
Neither variant is wrong — the report makes sure the choice is conscious.

## Example

```sql
ALTER TABLE dbo.Orders ADD CONSTRAINT FK_Orders_Customers
    FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (Id);
```

Reports Warning: `Adding FOREIGN KEY constraint FK_Orders_Customers scans every existing row
of dbo.Orders under a Sch-M lock (WITH CHECK is the default).`

```sql
ALTER TABLE dbo.Orders WITH NOCHECK
    ADD CONSTRAINT CK_Orders_Quantity CHECK (Quantity > 0);
```

Reports Info: `WITH NOCHECK skips validating existing rows … but CHECK constraint
CK_Orders_Quantity stays untrusted: the optimizer will not use it for plans.`

A default constraint (`ADD CONSTRAINT DF_… DEFAULT (0) FOR Status`) validates nothing and is
clean.

## How to fix

The zero-downtime pattern: add `WITH NOCHECK` now, validate later in a low-traffic window —
the double CHECK is not a typo:

```sql
ALTER TABLE dbo.Orders WITH CHECK CHECK CONSTRAINT CK_Orders_Quantity;
```

That statement scans with a less disruptive lock than the ADD and marks the constraint
trusted. Clean up violating rows first, or the validation fails.

## Assumptions (version / edition)

Not version or edition dependent (catalog row `add_check_or_fk`).
