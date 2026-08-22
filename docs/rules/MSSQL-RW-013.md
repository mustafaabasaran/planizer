# MSSQL-RW-013 — Creating or dropping a clustered index rewrites the entire table

**Default severity:** Critical (Warning + inconclusive for DROP INDEX) · **Category:** Rewrite vs metadata-only

## What it checks

- `CREATE CLUSTERED INDEX` — certain offline: without `DROP_EXISTING` the statement only
  succeeds when the table is currently a heap, so it *always* rewrites → **Critical**.
- `CREATE CLUSTERED INDEX … WITH (DROP_EXISTING = ON)` — also succeeds on a table that already
  has a clustered index. Still a full rewrite → **Critical**, but the message differs:
  nonclustered indexes are rebuilt only if the clustering key changes.
- `DROP INDEX` — decided from the script when the **same file created that index earlier**
  (`CREATE [UNIQUE] [NON]CLUSTERED INDEX <name> ON <table>`, matched by name and table
  regardless of quoting or an implicit `dbo`): a known clustered index is a certain rewrite →
  **Critical**; a known nonclustered one only deallocates pages → not reported. EF Core
  idempotent scripts accumulate every migration in one file, so this resolves most drops.
  A CREATE that comes *after* the drop (drop-and-recreate) proves nothing. Otherwise the
  script alone cannot tell whether the dropped index is the clustered one, so the rule reports
  **Warning + inconclusive** instead of staying silent.

## Why it matters

The clustered index **is** the table. Creating one on a heap physically re-sorts and rewrites
every row; dropping one turns the table back into a heap, again moving every row. Both also
**rebuild every nonclustered index** on the table, because their row locators change (RID ↔
clustering key). The real cost is therefore table size × (1 + number of NC indexes) — routinely
the single most expensive statement in a migration, behind an innocent-looking one-liner.

## Example

```sql
CREATE CLUSTERED INDEX IX_HeapTable_Id ON dbo.HeapTable (Id);
```

Reports: `Critical MSSQL-RW-013 … Creating clustered index IX_HeapTable_Id rewrites every row
of dbo.HeapTable and rebuilds all of its nonclustered indexes (without DROP_EXISTING the
statement only succeeds on a heap).` The `WITH (DROP_EXISTING = ON)` form reports instead:
`… recreates the clustered index of dbo.HeapTable, rewriting every row; nonclustered indexes
are rebuilt only if the clustering key changes.`

```sql
DROP INDEX IX_Orders_Code ON dbo.Orders;
```

Reports: `Warning MSSQL-RW-013 … Cannot determine offline whether IX_Orders_Code is the
clustered index of dbo.Orders … verify before running. [inconclusive]`

```sql
CREATE CLUSTERED INDEX IX_Heap_Id ON dbo.Heap (Id);
GO
DROP INDEX IX_Heap_Id ON dbo.Heap;
```

The drop reports `Critical MSSQL-RW-013 … Dropping clustered index IX_Heap_Id (created earlier
in this file) turns dbo.Heap back into a heap …` — conclusive, because the file itself says the
index is clustered. Had the CREATE been `NONCLUSTERED` (or a plain `CREATE INDEX`), the drop
would not be reported at all.

`CREATE NONCLUSTERED INDEX` is judged by MSSQL-LOCK-002 instead.

## How to fix

Treat it as the full rewrite it is: maintenance window, or `ONLINE = ON` on Enterprise
(MSSQL-LOCK-002 fixes apply). Order matters — create the clustered index *before* the
nonclustered ones on a new table, never after. For the DROP INDEX case, verify first:

```sql
SELECT type_desc FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.Orders') AND name = 'IX_Orders_Code';
```

## Assumptions (version / edition)

A rewrite on every version and edition (catalog rows `create_clustered_index_on_heap`,
`drop_clustered_index`). With a schema snapshot (Phase 2) the DROP INDEX branch becomes
conclusive.
