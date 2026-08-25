# Planizer — Rule Engine Scope

A safety analyzer for MSSQL migrations / DDL. Every rule is deterministic (no LLM):
ScriptDom parser + rule tables + system views.

Layers (the markers used throughout this document and in the rule pages):
- **(S)** SQL-text-only — works from the SQL text alone, no server required
- **(SCH)** Schema — needs schema information: a live connection or a schema snapshot (JSON)
- **(STAT)** Statistics — needs statistics / server information: a live connection

Phase plan:
- **Phase 1:** Sections 1, 2, 3, 4 + Section 9 (JSON + text output). Server-less CLI.
- **Phase 1.5:** Sections 5, 6.
- **Phase 2:** Sections 7, 8 + PerformanceStudio integration (DML / query plan analysis).

---

## 1. Parse and classification layer (S)

- Convert T-SQL to an AST with ScriptDom; split into batches (GO).
- **Statements that run at deploy time:** the batch's top-level statements **+** the statements
  inside IF/ELSE, BEGIN…END, WHILE and TRY/CATCH bodies (recursive, pre-order: the wrapper first,
  then its children). Rules see this flattened list; every statement carries its batch order, depth,
  parent and nearest IF / TRY / CATCH / WHILE context. Module bodies (inside CREATE/ALTER
  PROCEDURE / FUNCTION / TRIGGER / VIEW) are **not flattened** — they are definitions, not
  migration actions. A `planizer:ignore` placed on a wrapping block also covers the statements in
  its body.
- Classify every statement: DDL / DML / DCL / control flow / dynamic SQL (EXEC, sp_executesql).
- Flag dynamic SQL and variable-based object names as "cannot be analyzed, manual review required".
- Extract the affected objects: table, index, constraint, view, proc; normalize as schema + name.
- Accept a compatibility level / target version parameter (2014, 2016, 2019, 2022, Azure SQL); most rules vary by version.
- Accept a target edition parameter (Enterprise / Standard / Express / Azure); online operations and metadata-only behaviors depend on it.

## 2. Lock and blocking analysis (S)

- The lock level taken by each DDL statement: Sch-M (schema modification, blocks everything), Sch-S, S, IX.
- Statements that take Sch-M: ALTER TABLE (most forms), CREATE/DROP INDEX (offline), ALTER INDEX REBUILD (offline), DROP TABLE, TRUNCATE, sp_rename, ALTER TABLE SWITCH.
- Offline CREATE INDEX: nonclustered takes an S lock on the table (reads allowed, no writes); clustered takes Sch-M (nothing allowed).
- Is ONLINE = ON present? If not and the edition supports it, suggest it. Online index operations are Enterprise only (plus Azure and Developer).
- Even with ONLINE = ON, short table locks remain: the preparation phase takes S, and the final phase takes S for a nonclustered create or Sch-M for a clustered create/drop or any rebuild (the duration-held Sch-M is an INDEX_OPERATION object lock that blocks concurrent DDL, not DML). Check whether WAIT_AT_LOW_PRIORITY (2014+) is used, and suggest it if not.
- Can RESUMABLE = ON be used (REBUILD 2017+, CREATE 2019+)? Suggest it for long-running index operations.
- ALTER INDEX REORGANIZE is always online; flag the cases where it could be suggested instead of REBUILD.
- Multiple Sch-M locks within the same transaction: the lock is held until the transaction ends; compute the total blocking window and warn.
- Locks taken on different tables in a different order within the same script: deadlock potential.
- Lock escalation risk: a large UPDATE/DELETE escalating to a table lock (~5,000-row threshold); suggest batching.
- DDL in the middle of an open transaction: is LOCK_TIMEOUT set? If not, risk of waiting forever.

## 3. Table rewrite vs metadata-only (S + edition)

The least understood and most prod-breaking area in MSSQL. The core: a **statement × version × edition → behavior** table.

- ADD COLUMN nullable, no default: always metadata-only.
- ADD COLUMN NOT NULL + DEFAULT: metadata-only on Enterprise (if the default is a runtime constant); on Standard/Express the whole table is written. A runtime constant is an expression evaluated "once per statement", independent of determinism: statement-level functions such as GETDATE()/SYSDATETIME()/CURRENT_TIMESTAMP are runtime constants and keep the fast path; NEWID()/NEWSEQUENTIALID(), evaluated per row, break metadata-only on every edition.
- ADD COLUMN NOT NULL, no default, table has data: fails with an error.
- ALTER COLUMN type widening: widening varchar/nvarchar/varbinary is metadata-only; a fixed-length change (int→bigint) is a rewrite; a precision/scale change is a rewrite.
- ALTER COLUMN type narrowing: data loss + full scan + failure risk.
- ALTER COLUMN NULL→NOT NULL: full scan (validation); fails if NULLs exist.
- ALTER COLUMN NOT NULL→NULL: metadata-only.
- ALTER COLUMN collation change: rewrite + the dependent indexes are recreated.
- ALTER COLUMN on a column that has an index/constraint/computed column/statistics on it: fails; those must be dropped first.
- DROP COLUMN: metadata-only, but the space is not reclaimed; suggest DBCC CLEANTABLE or a rebuild.
- Adding a PERSISTED computed column: full scan + write.
- DATA_COMPRESSION change: rewrite.
- Creating/dropping a clustered index on a heap: the whole table and all nonclustered indexes are rewritten.
- Clustered index key change: all nonclustered indexes are rebuilt.
- ADD CONSTRAINT CHECK / FOREIGN KEY: existing data is scanned (under Sch-M); WITH NOCHECK skips the scan but the constraint stays "untrusted" and the optimizer cannot use it. Report both options with their trade-off.
- ADD PRIMARY KEY / UNIQUE: creates an index; the offline rules apply.
- Will the row size exceed 8,060 bytes? (Conclusive with SCH; a warning in S.)

## 4. Reversibility and data loss (S)

- Irreversible statements: DROP TABLE, DROP COLUMN, TRUNCATE, DELETE without WHERE, ALTER COLUMN narrowing, DROP INDEX (no data lost, but there is a rebuild cost).
- Try to generate the automatic inverse statement for each statement (ADD COLUMN → DROP COLUMN, CREATE INDEX → DROP INDEX, ADD CONSTRAINT → DROP CONSTRAINT). If one cannot be generated, flag "rollback script required".
- Suggest a backup/rename before DROP: the "rename the column to `_deprecated` first, drop it one release later" pattern.
- Conformance to the expand/contract pattern: can the old and new application versions run at the same time? Renames and drops break this.
- sp_rename: references inside views, procs and functions are not updated (deferred name resolution); they blow up at run time. Column renames are especially dangerous.
- TRUNCATE: can be rolled back inside a transaction, but does not work on a table referenced by a FK.
- Identity reseed; forgetting SET IDENTITY_INSERT.

## 5. Failure risk: will the script fail in prod? (S; made conclusive with SCH)

- Idempotency: are IF EXISTS / IF NOT EXISTS / DROP IF EXISTS (2016+) / CREATE OR ALTER (2016 SP1+) used? If not, the script blows up on the second run.
- ADD COLUMN followed by an UPDATE of that column in the same batch: compile-time error; a GO is required.
- Variables lost after GO.
- More than one CREATE PROCEDURE/VIEW/TRIGGER in a single batch (each must be alone in its batch).
- String literal type mismatch (a varchar literal into an nvarchar column).
- Column name / reserved word collisions; missing square brackets.
- Index key size limit: 900 bytes clustered, 1,700 bytes nonclustered (2016+); 32 columns maximum.
- The 999 nonclustered indexes per table limit (SCH).
- Using LOB/max types as index keys (they must go into INCLUDE).
- Version mismatch: syntax that does not exist in the target version (STRING_AGG 2017, DROP IF EXISTS 2016, RESUMABLE 2017/2019, ledger 2022).

## 6. Transaction and script hygiene (S)

- Is SET XACT_ABORT ON present? If not, the transaction may stay open after an error.
- BEGIN TRAN / COMMIT pairing; is there a ROLLBACK inside TRY/CATCH?
- GO inside a transaction (the transaction carries across batches, but error handling gets messy).
- A single huge transaction: locks are held for the whole migration; steps that could be split into separate transactions.
- SET NOCOUNT ON, SET ANSI_NULLS / QUOTED_IDENTIFIER settings (mandatory for filtered indexes and indexed views).
- USE [db] or a cross-database reference: dangerous if the migration tool already sets the context.
- Hardcoded server / linked server names.
- Absence of progress messages via PRINT/RAISERROR (in long migrations).

## 7. Schema-dependent checks (SCH)

With a live connection or a previously captured schema snapshot (JSON):

- Does the object exist / does it already exist (the conclusive form of idempotency).
- Is the table a heap? On a heap: RID lookups, forwarded records, the cost of adding a clustered index.
- Current row size of the table + the new column > 8,060?
- Is the added index a duplicate or a subset of an existing index (duplicate/overlapping index)?
- Does the referencing column of the added FK have an index? If not, DELETEs on the parent table scan.
- Which index/constraint/computed column/statistics/default depend on the column being altered (what must be dropped first).
- Does a SCHEMABINDING view or function lock the table? The ALTER fails.
- Dependency tree: which procs/views/functions use this column (sys.sql_expression_dependencies). The list of what will break on rename/drop.
- Is the table under replication, CDC, Change Tracking, Always Encrypted, temporal (system-versioned) or ledger? Each has DDL restrictions (temporal changes propagate to the history table; in CDC the new column does not enter the capture instance).
- Is it a memory-optimized table? Most ALTERs are unsupported or offline.
- Is there a columnstore index? ALTER COLUMN and some operations are forbidden.
- Is the table partitioned? Is the index aligned; are the SWITCH conditions satisfied?
- Are there triggers? DML migration steps fire the trigger.
- Is the column already nullable, already of that type (no-op statement detection).
- Existing untrusted constraints (a WITH NOCHECK history).
- Permission check: does the executing user have ALTER permission.

## 8. Statistics-dependent: duration and impact estimation (STAT)

- Table row count and page count (sys.dm_db_partition_stats) → a rough duration estimate for operations that need a rewrite/scan (with MB/s calibration).
- Estimated size for index creation (column types × row count × fill factor) and tempdb/log needs (SORT_IN_TEMPDB).
- Transaction log growth estimate (under FULL recovery, rewrite operations bloat the log).
- Activity level of the table (sys.dm_db_index_usage_stats): Sch-M is more dangerous on a heavily read/written table; a "run during low-traffic hours" suggestion.
- Index usage statistics: has the index about to be dropped with DROP INDEX been used in the last 30 days?
- Overlap between the added index and the missing index suggestions (sys.dm_db_missing_index_details).
- Fragmentation: whether REBUILD or REORGANIZE is needed (the 5–30% rule).
- Query Store: the most expensive queries depending on this table/column, and a baseline record for tracking plan regressions after the migration.
- Disk space: is the temporary space required for a rewrite (~2x the table) available?
- Obtaining the estimated plan: capture the plan of the DML steps with SET SHOWPLAN_XML ON without executing them; hand it over to the PerformanceStudio rules in Phase 2.

## 9. Output and report model (common)

- For every finding: rule ID, severity (Info / Warning / Critical / Blocker), statement location (line:column), a one-sentence rationale, a suggested fix (corrected SQL where possible), and the version/edition assumption under which it was produced.
- Script-level summary: total blocking window, number of irreversible steps, estimated duration, disk/log required.
- Output formats: JSON (CI / LLM consumers), text, SARIF (GitHub code scanning), Markdown (PR comment).
- Break CI via the exit code; the severity threshold is configurable.
- Per-rule suppression: the `-- planizer:ignore RULE_ID reason` comment.
- Config file: target version, edition, enabling/disabling rules, severity overrides, table size thresholds.
- Schema snapshot generator: capture the sys.* views from prod into JSON once, then run the SCH layer in CI without a connection.

---

## First steps

1. Extract the rule table of Section 3 as a CSV: `statement, version, edition, lock, rewrite?, reversible?, note`.
2. Parse the .sql file with ScriptDom; for every statement output "DDL or DML" and, if DDL, "which lock". Nothing else.
3. Run 5 of your own migrations through this first version; open an issue for every place where it stays silent.
