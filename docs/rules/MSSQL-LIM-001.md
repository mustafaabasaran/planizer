# MSSQL-LIM-001 — Index key exceeds the column-count or byte-size limit

**Default severity:** Blocker · Critical when only a variable-length maximum exceeds the limit · **Category:** Failure risk

## What it checks

Every index key the file creates — `CREATE [UNIQUE] [CLUSTERED] INDEX`, `PRIMARY KEY` and
`UNIQUE` constraints (table-level or column-level, in `CREATE TABLE` or `ALTER TABLE … ADD`) and
inline `INDEX` definitions — against SQL Server's key limits:

| Check | Limit | Severity | Server error |
|---|---|---|---|
| Key column count | 32 from SQL Server 2016; 16 before | Blocker | 1904 at CREATE |
| LOB / MAX key column (`varchar(max)`, `nvarchar(max)`, `varbinary(max)`, `xml`, `text`, `ntext`, `image`) | never allowed in a key | Blocker | 1919 at CREATE |
| Key size, fixed-width columns alone already over the limit | 900 bytes clustered; 1700 bytes nonclustered from 2016 (900 before) | Blocker | 1944 at CREATE |
| Key size, only the declared maximum of variable-length columns over the limit | same | Critical | 1946 at the first INSERT/UPDATE that produces a longer key |

The column count needs no type information and is always checked. The byte and LOB checks need
the column types, which offline come from a `CREATE TABLE` or `ALTER TABLE … ADD` of the same
table **in the same file**; when the table is not defined there — or a key column is computed or
of a type with no fixed maximum (`sql_variant`, CLR) — those checks stay silent rather than
reporting "inconclusive" on every index of a migration. `INCLUDE` columns do not count. Hash
(memory-optimized) and columnstore indexes have other limits and are skipped. The type widths are
the ones MSSQL-RW-016 uses for row width.

## Why it matters

The Blocker cases fail at deploy time, which is bad but visible. The Critical case is the time
bomb: a nonclustered key on `nvarchar(900)` **creates fine** (with a warning nobody reads in a
pipeline log) and then, weeks later, the first user who types a 1 000-character value gets
**error 1946, "Operation failed. The index entry of length 1800 bytes for the index 'IX_Names_Path'
exceeds the maximum length of 1700 bytes"** — on an INSERT in the application, not in the
migration. A real example from the corpus scan: a primary key over `nvarchar(450) + tinyint +
nvarchar(100)`, 1 101 bytes against the 900-byte clustered limit.

## Example

```sql
CREATE TABLE dbo.Names
(
    Id int NOT NULL,
    Name nvarchar(450) NOT NULL,
    Path nvarchar(900) NULL
);
CREATE UNIQUE CLUSTERED INDEX IX_Names_Name ON dbo.Names (Name, Id);
CREATE INDEX IX_Names_Path ON dbo.Names (Path);
```

Reports on the clustered index: `Critical MSSQL-LIM-001 Key of index IX_Names_Name on dbo.Names
can reach 904 bytes; the clustered key limit is 900 bytes — CREATE succeeds with a warning, but
any INSERT/UPDATE producing a longer key fails (error 1946).` and on the second: `… Key of index
IX_Names_Path on dbo.Names can reach 1800 bytes; the nonclustered key limit is 1700 bytes …`.

The other shapes: `CREATE INDEX IX_Wide ON dbo.Wide (C01, …, C33);` → `Blocker MSSQL-LIM-001
index IX_Wide on dbo.Wide has 33 key columns; SQL Server 2019 allows at most 32 (error 1904 at
CREATE time).` (no CREATE TABLE needed); a key on `Body nvarchar(max)` → `Blocker … Key column
Body (nvarchar(max)) of index IX_Documents_Body on dbo.Documents uses a LOB/MAX type, which
cannot be an index key column (error 1919).`; `Code char(1000) … PRIMARY KEY (Code)` → `Blocker
… Key of PRIMARY KEY PK_Codes on dbo.Codes is at least 1000 bytes; the clustered key limit is 900
bytes, so CREATE fails (error 1944).`

Quiet: `nvarchar(850)` (exactly 1 700 bytes) in a nonclustered key on 2016+, a MAX column placed
in `INCLUDE`, and any index whose table is not defined in the file.

## How to fix

Keep only the columns that are searched or sorted in the key and move the rest to `INCLUDE`:

```sql
CREATE INDEX IX_Names_Path ON dbo.Names (Id) INCLUDE (Path);
```

For a wide text column that must be searchable, index a bounded prefix or a hash through a
computed column:

```sql
ALTER TABLE dbo.Documents ADD BodyPrefix AS LEFT(Body, 450) PERSISTED;
CREATE INDEX IX_Documents_BodyPrefix ON dbo.Documents (BodyPrefix);
```

Or shorten the column (`nvarchar(450)` is the largest `nvarchar` that fits a clustered key).

## Assumptions (version / edition)

`--target-version` decides the limits: 16 key columns and a 900-byte nonclustered key before
2016, 32 and 1 700 bytes from 2016 on; the clustered limit is 900 bytes everywhere. Edition
independent. Byte sizes are computed from the declared types (`nvarchar(n)` = 2 n bytes,
`varchar(n)` = n, `int` = 4, …); the server additionally charges a few bytes of row overhead, so a
key that is within a handful of bytes of the limit is worth a look even when the rule is quiet.
