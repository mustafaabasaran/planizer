# MSSQL-VER-001 — Feature not available on the target SQL Server version

**Default severity:** Blocker · Warning (inconclusive) when only a service pack is in doubt · **Category:** Failure risk

## What it checks

Whether the script uses something the SQL Server named by `--target-version` does not have. Two
detection paths share the rule id:

**Grammar.** The script is parsed with the target version's grammar. When that fails, it is
parsed again with each newer grammar ScriptDom ships — 2016 → 2017 → 2019 → 2022 → 2025 →
post-2025 preview — and the first one that accepts it is named in the finding: the statement is
reported as VER-001 instead of MSSQL-PARSE-001 — "syntax not supported by the SQL Server 2014
grammar; it first parses with the SQL Server 2016 grammar" — and analysis **continues with the
newer parse**, so the lock, rewrite and reversibility rules still see the statement. Syntax only
the post-2022 grammars accept is reported without a `--target-version` suggestion ("the syntax
needs a SQL Server newer than 2022"). Only when *no* grammar accepts the file does it stay a
MSSQL-PARSE-001. This is how `DROP … IF
EXISTS` against 2014, or `TRIM(LEADING … FROM …)`, `IS DISTINCT FROM`, the `WINDOW` clause and
`LEDGER = ON` against 2019, are caught.

**Feature catalog.** Many features are ordinary function calls or options that every grammar
accepts — `STRING_SPLIT` is just an identifier to the 2014 parser. `mssql-feature-versions.csv`
maps them to the version that introduced them:

| Introduced in | Features |
|---|---|
| 2016 | `STRING_SPLIT`, `OPENJSON`, `JSON_VALUE`, `JSON_QUERY`, `JSON_MODIFY`, `ISJSON`, `COMPRESS`, `DECOMPRESS`, `DATEDIFF_BIG`, `STRING_ESCAPE`, `SESSION_CONTEXT`, `AT TIME ZONE`, `DROP … IF EXISTS` (grammar path) |
| 2016 SP1 | `CREATE OR ALTER` procedure / function / view / trigger |
| 2017 | `STRING_AGG`, `TRIM`, `CONCAT_WS`, `TRANSLATE`, `ALTER INDEX … REBUILD WITH (RESUMABLE = ON)` |
| 2019 | `APPROX_COUNT_DISTINCT`, `CREATE INDEX … WITH (RESUMABLE = ON)` |
| 2022 | `GREATEST`, `LEAST`, `DATE_BUCKET`, `DATETRUNC`, `GENERATE_SERIES`, `JSON_PATH_EXISTS`, `JSON_OBJECT`, `JSON_ARRAY`, `APPROX_PERCENTILE_CONT/DISC`, `BIT_COUNT`, `GET_BIT`, `SET_BIT`, `LEFT_SHIFT`, `RIGHT_SHIFT`, two-argument `LTRIM` / `RTRIM`, three-argument `STRING_SPLIT`, `CREATE INDEX … WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (…)))`; grammar path: `TRIM(LEADING|TRAILING|BOTH …)`, `IS [NOT] DISTINCT FROM`, `WINDOW`, `LEDGER = ON` |

(`WAIT_AT_LOW_PRIORITY` on `ALTER INDEX … REBUILD` has been there since 2014 and is not
reported.) Target older than the feature → Blocker. Target exactly 2016 and the feature is from
2016 **SP1** (`CREATE OR ALTER`) → Warning marked inconclusive, because the patch level cannot be
known offline. Only bare built-in calls are gated: `dbo.STRING_AGG(…)` or `util.TRIM(…)` is a
user-defined function. Module bodies are scanned, because a procedure that references an unknown
function fails at `CREATE` time. With `--target-version azure` nothing is gated. When both paths
fire on one statement, the catalog finding (which names the feature) is kept and the grammar one
dropped.

## Why it matters

Development runs on the newest Developer edition; the customer runs what they licensed four years
ago. The script parses, the tests pass, and the first thing production says is **error 195,
"'STRING_AGG' is not a recognized built-in function name"** — or `Incorrect syntax near 'IF'` for
`DROP TABLE IF EXISTS` on 2014. `--target-version` exists so this is found before the deployment,
not during it.

## Example

With `--target-version 2016`:

```sql
SELECT STRING_AGG(Name, ',') FROM dbo.T;
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON, RESUMABLE = ON);
```

Reports: `Blocker MSSQL-VER-001 STRING_AGG() requires SQL Server 2017; the target is SQL Server
2016, where the statement fails.` with the fix `Raise --target-version to 2017 if production
really runs it; otherwise replace STRING_AGG() with an equivalent SQL Server 2016 supports.` —
and `Blocker MSSQL-VER-001 RESUMABLE INDEX REBUILD (ALTER INDEX … REBUILD WITH (RESUMABLE = ON))
requires SQL Server 2017; …`.

With `--target-version 2014`, `DROP TABLE IF EXISTS dbo.Old;` takes the grammar path: `Blocker
MSSQL-VER-001 Syntax not supported by the SQL Server 2014 grammar; it first parses with the SQL
Server 2016 grammar: Incorrect syntax near 'IF'.` — and MSSQL-LOCK-001 still reports the Sch-M
lock of the DROP TABLE on the same line, because the statement was analysed with the 2016 parse.

With `--target-version 2016`, `CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1;` reports `Warning
MSSQL-VER-001 CREATE OR ALTER PROCEDURE requires SQL Server 2016 SP1; the target is a bare SQL
Server 2016 whose patch level is unknown offline. [inconclusive]`.

## How to fix

If production really runs the newer version, say so — in `.planizer.json` or on the command line:

```json
{ "targetVersion": "2017" }
```

Otherwise rewrite with what the target has: `STRING_AGG` → `FOR XML PATH('')` / `STUFF`; `DROP
TABLE IF EXISTS dbo.Old` → `IF OBJECT_ID(N'dbo.Old', N'U') IS NOT NULL DROP TABLE dbo.Old`;
`CREATE OR ALTER` → guarded `DROP` + `CREATE` in its own batch; `TRIM(x)` → `LTRIM(RTRIM(x))`;
`RESUMABLE = ON` → drop the option (and plan for a restart from scratch if the build is
interrupted).

## Assumptions (version / edition)

Entirely driven by `--target-version` (default 2019); a target at or above the feature's version
is never reported. Edition independent — whether the feature is *allowed* on the edition (ONLINE
index builds on Standard, for instance) is MSSQL-LOCK-003's job. Features ScriptDom cannot
distinguish from a plain identifier and that are not in the catalog are not detected; add a row
to `mssql-feature-versions.csv` (`feature_key,detect,pattern,min_version,note`) to cover a new
one.
