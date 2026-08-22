# MSSQL-LOCK-003 — ONLINE = ON is not available on this edition

**Default severity:** Blocker · **Category:** Locking

## What it checks

Any index operation (`CREATE INDEX`, `ALTER INDEX … REBUILD`) that specifies `ONLINE = ON`
while the configured `--edition` is **Standard** or **Express**.

## Why it matters

Online index operations are an Enterprise (and Azure) feature. On Standard or Express the
statement does not degrade to an offline build — it **fails outright with error 1712**, and the
migration stops right there. This is one of the most common "worked in dev (Developer edition =
Enterprise behavior), died in prod (Standard)" traps.

## Example

```sql
-- analyzed with --edition standard
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
```

Reports: `Blocker MSSQL-LOCK-003 … ONLINE = ON requires Enterprise edition (or Azure); on
standard this statement fails with error 1712.`

The same applies to `ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);` on Express.

## How to fix

Either of:

- remove `ONLINE = ON` (accepting the offline blocking behavior — see MSSQL-LOCK-002 /
  MSSQL-LOCK-006), or
- correct the edition assumption (`--edition enterprise`) if the target server really is
  Enterprise or Azure.

## Assumptions (version / edition)

Fires only when `--edition` is `standard` or `express`. `developer` maps to Enterprise
behavior and does not fire — which is precisely why this must be validated against the
*production* edition, not the dev box.
