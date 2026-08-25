# Planizer — Roadmap

A tool that validates and explains SQL changes (migrations, DDL, DML, queries) before they run.
Every check is deterministic: parser + rule table + system views. No LLM.

The output always has the same shape: **rule ID + severity + location + one-sentence rationale + suggested fix.**

For the detailed rule list, see [RULES.md](RULES.md).

---

## Design principles

- **One engine, three source modes:** offline (SQL only) / snapshot (SQL + schema JSON) / live (SQL + connection). Rules are fed through provider interfaces (`ISchemaProvider`, `IStatsProvider`); when data is missing, a rule reports "inconclusive" instead of staying silent.
- **The core is dialect-agnostic.** The report model, the rule interface and the provider interfaces live in `Planizer.Core`; MSSQL and Postgres are adapters.
- **Don't rewrite what already exists.** The parts solved by Squawk (Postgres DDL) and PerformanceStudio (MSSQL plan analysis) are first wired in as adapters and, if needed, brought in-house later.
- **CLI first, everything else after.** GUI, IDE extension and MCP server are all built on the same Core.
- **Every rule has a test:** at least one triggering and one non-triggering .sql fixture.

## Technology

| Layer | Choice |
|---|---|
| Language / runtime | C# / .NET 8+ (Native AOT single binary) |
| MSSQL parser | Microsoft.SqlServer.TransactSql.ScriptDom |
| Postgres parser | Phase 4: Squawk adapter → later a libpg_query binding |
| DB access | Microsoft.Data.SqlClient, Npgsql |
| CLI | System.CommandLine |
| Output | System.Text.Json, SARIF 2.1.0 (hand-written with `Utf8JsonWriter` — no Sarif SDK, see [ADR-0002](adr/0002-sarif-handwritten.md)), Markdown |
| Tests | xUnit + .sql fixtures; Testcontainers (SQL Server / Postgres) |
| Distribution | dotnet tool, GitHub Releases binary, Docker image |
| MCP | ModelContextProtocol C# SDK (Phase 3) |

## Project layout

```
src/
  Planizer.Core        report model, IRule, ISchemaProvider, IStatsProvider, config
  Planizer.MsSql       ScriptDom adapter + MSSQL rules + rule tables (CSV)
  Planizer.Postgres    Squawk adapter (Phase 4)
  Planizer.Cli         the planizer command
tests/
  Planizer.Tests       rule tests + fixtures/
docs/
  RULES.md, ROADMAP.md
```

---

## Phase 0 — Skeleton (1 week)

Goal: an end-to-end working CLI with a single rule.

- [x] Solution + project layout
- [x] `Planizer.Core`: `Finding`, `Severity`, `Report`, `IRule`, `IAnalysisContext`, provider interfaces (empty implementations)
- [x] `Planizer.MsSql`: parse a .sql file with ScriptDom, split it into batches, classify every statement as DDL/DML/other
- [x] First rule: `MSSQL-LOCK-001` — report DDL statements that take a Sch-M lock
- [x] CLI: `planizer analyze <file|dir> --dialect mssql --output json|text`
- [x] Exit code: based on a severity threshold
- [x] xUnit + first fixtures
- [x] GitHub Actions CI

**Exit criterion:** you can feed in one of your own migration files and get a lock report.

## Phase 1 — MSSQL DDL safety, offline (4–6 weeks)

Goal: migration analysis that runs without a server and that nobody else does for MSSQL. RULES.md sections 1–4.

- [x] **Rule table (CSV):** statement × version × edition → lock level, rewrite/metadata-only, reversibility. The heart of the project; this comes first.
- [x] Config: `--target-version`, `--edition`, `.planizer.json`
- [x] Lock and blocking rules (section 2): Sch-M, ONLINE, RESUMABLE, WAIT_AT_LOW_PRIORITY, multiple Sch-M windows, lock escalation
- [x] Rewrite vs metadata-only rules (section 3): ADD/ALTER/DROP COLUMN variants, constraint creation, clustered index, compression
- [x] Reversibility rules (section 4): irreversible statements, automatic reverse-script generation, sp_rename, expand/contract
- [x] Suppress comment: `-- planizer:ignore RULE_ID reason`
- [x] Make the text output readable; script-level summary (total blocking window, number of irreversible steps)
- [x] Markdown output (for PR comments)
- [x] Dynamic SQL detection and "cannot be analyzed" marking
- [x] README + rule documentation (one page per rule: what, why, how to fix)
- [ ] Publish as a `dotnet tool` (v0.1) — the package and a local-install smoke test are ready; the NuGet.org release has not been done because it requires an API key

**Exit criterion:** an acceptable false-positive rate on 10 real migrations; at least one team uses it in CI.

## Phase 1.5 — Script hygiene and failure risk (2 weeks)

RULES.md sections 5–6.

- [x] Prerequisite: nested statements (IF/ELSE, BEGIN…END, WHILE, TRY/CATCH bodies) are flattened and
      analyzed with batch and control-flow context; module bodies are not entered
- [x] Idempotency checks (IF EXISTS, CREATE OR ALTER) — `MSSQL-IDEM-001..003`
- [x] Batch/GO errors — `MSSQL-BATCH-001..002`; no separate rule was needed for "multiple CREATE PROC
      in one batch": ScriptDom reports it as a parse error, which `MSSQL-PARSE-001` covers
- [x] Non-Unicode literals (non-ASCII text without the `N` prefix) — `MSSQL-LIT-001`
- [x] Transaction hygiene: XACT_ABORT, TRY/CATCH, BEGIN/COMMIT matching, transactions that span GO and
      long transactions — `MSSQL-TRAN-001..006`
- [x] SET options (QUOTED_IDENTIFIER/ANSI_NULLS, NOCOUNT) and environment dependencies (USE, linked
      server / cross-database, progress messages) — `MSSQL-SET-001..002`, `MSSQL-ENV-001..003`
- [x] Version incompatibility (syntax / function / option missing in the target version) — `MSSQL-VER-001` +
      `mssql-feature-versions.csv`; syntax that fails in the target grammar but passes in a newer grammar
      is reported as VER-001 instead of PARSE-001 and analysis continues
- [x] Index key size / column count limits, identifier length — `MSSQL-LIM-001..002`
      (LIM-001: Blocker if the fixed-width part alone exceeds the limit = error 1944 at CREATE; Critical if
      only the variable-length maximum exceeds it = error 1946 on the first long INSERT/UPDATE; LIM-002: the
      116 limit applies only to local `#` temp tables, `##` gets 128)
- [x] SARIF output → GitHub code scanning (`--output sarif`, `--sarif-file`; ADR-0002)
- [x] GitHub Action (composite `action.yml`, builds from source) + CI self-check (`samples/` → SARIF
      upload)
- [x] Re-scan of a private corpus of 24 production repositories (`scripts/corpus-scan.sh`); no
      false-positive class found in the new rule families (counts in CLAUDE.md)
- [x] Review round (fact-check + scope + e2e): 18 findings fixed — ENV-002 double counting in nested
      statements, TRAN-002 GOTO-label error path and `WHILE @@TRANCOUNT`, IDEM-001..003 exit-guard idiom,
      LIM-002 `##`, TRAN-004 savepoint, IDEM-002 unnamed constraint behavior, IDEM-003 `SELECT INTO`,
      SET-002 write statements only, BATCH-001 derived-table alias; documentation sync

**Exit criterion:** the new rule families are noise-free on a real migration corpus; SARIF shows up in code
scanning; the action can be invoked from a repo via `uses:`.

## Phase 2 — Schema and statistics awareness (4–6 weeks)

RULES.md sections 7–8. The snapshot and live modes are enabled.

- [ ] `planizer snapshot --server ... --out schema.json`: schema + row/page counts from the sys.* views
- [ ] `ISchemaProvider` snapshot and live implementations
- [ ] Schema-dependent rules: object existence, heap, row size, duplicate index, missing FK index, SCHEMABINDING, dependency tree
- [ ] Special-feature detection: temporal, CDC, replication, columnstore, memory-optimized, partition
- [ ] `IStatsProvider`: duration estimate (rows × pages × calibration), log/tempdb/disk requirements
- [ ] Severity escalation based on table activity level
- [ ] Usage-statistics check before DROP INDEX
- [ ] Integration tests with Testcontainers
- [ ] Container-based verification of the DDL behavior catalog (Developer = Enterprise, and Express),
      run in CI on native amd64. Two rows must be settled **empirically**, because Microsoft's own
      pages disagree with each other:
      (a) does an `INSERT` succeed while an **offline nonclustered** `CREATE INDEX` is running?
      (the `ALTER TABLE` index_option page and the lock compatibility matrix say no — writes are
      blocked by the table-level S lock — while the February 2025 rewrite of the `CREATE INDEX`
      page reads as if they were not; MSSQL-LOCK-002 currently follows the former);
      (b) which lock does the **final phase of an online nonclustered `CREATE INDEX`** take?
      ("How online index operations work" says a short shared (S) lock, and Sch-M only for a
      clustered create/drop or any rebuild; MSSQL-LOCK-004 and the
      `create_nonclustered_index_online` catalog row currently follow that)

**Exit criterion:** you can say "this migration will take an estimated X seconds and Y MB of log in prod".

## Phase 3 — DML and query plan analysis (6–8 weeks)

PerformanceStudio's territory. Adapter first, native later.

- [ ] **3a — Adapter:** in live mode, fetch the estimated plan (SHOWPLAN_XML) for DML/SELECT statements, hand it to the PerformanceStudio CLI as `.sqlplan`, and convert its JSON output to the Planizer report model. DDL + DML findings in a single report.
- [ ] **3b — Native plan analysis:** `Planizer.MsSql.Plans` — .sqlplan XML parser + rule set. Starting from PerformanceStudio's 30 rules: memory grant, row estimate skew, missing index, spill, parallel skew, key/RID lookup, implicit conversion, scalar UDF, parameter sniffing, anti-patterns
- [ ] Static DML rules (no plan needed): UPDATE/DELETE without WHERE, SELECT *, non-SARGable predicate, leading wildcard, NOT IN with nullable, implicit conversion candidates, batching suggestion for large DELETEs
- [ ] Plan comparison: Query Store baseline before/after a migration, regression detection
- [ ] Query Store integration: pull and analyze the most expensive queries
- [ ] MCP server: `analyze_migration`, `analyze_plan`, `explain_finding`, `get_lock_profile`
- [ ] (Optional) GUI: plan visualization — only after the CLI/MCP have settled

**Exit criterion:** the migration + new queries in a PR are evaluated in a single report; an AI agent can call it over MCP.

## Phase 4 — PostgreSQL (4–6 weeks)

- [ ] `Planizer.Postgres`: Squawk adapter — convert Squawk's JSON output to the Planizer report model. Quick win.
- [ ] libpg_query .NET binding (P/Invoke + protobuf) — to write natively the rules Squawk does not cover
- [ ] Postgres lock table: ACCESS EXCLUSIVE vs SHARE UPDATE EXCLUSIVE, CONCURRENTLY, NOT VALID + VALIDATE, lock_timeout
- [ ] Postgres-specific: ADD COLUMN DEFAULT (metadata-only on 11+), SET NOT NULL + CHECK pattern, enum changes, VACUUM/bloat awareness
- [ ] EXPLAIN (FORMAT JSON) analysis: Seq Scan, nested loop, sort spill, missing index estimate
- [ ] `ISchemaProvider`/`IStatsProvider` Postgres implementations (pg_catalog, pg_stat_*)
- [ ] Automatic dialect detection

## Phase 5 — Ecosystem

- [ ] Migration framework integrations: EF Core migrations (generate SQL script → analyze), Flyway, Liquibase, DbUp
- [ ] VS Code / Rider extension (LSP)
- [ ] SSMS / Azure Data Studio extension
- [ ] Azure SQL / Managed Instance edition differences
- [ ] Rule packs: an API for writing team-specific custom rules
- [ ] Web report / dashboard (optional)

---

## Out of scope (for now)

- MySQL / MariaDB / Oracle / SQLite
- Query rewriting / automatic optimization (suggestions yes, automatic changes no)
- LLM-based explanations — rules are deterministic; an LLM would only ever be a separate, optional layer on top of the report, later
- Schema diff / migration generation (the territory of Atlas and pg-schema-diff)

## Open questions

- Validating the edition-dependent behaviors in the rule table: a real test of every row in Docker against Enterprise (Developer) and Express — including the two rows where the documentation contradicts itself (offline nonclustered build: are writes blocked? online nonclustered create: S or Sch-M in the final phase?), see Phase 2
- Can PerformanceStudio's Core be referenced as a NuGet package, or is it integrated through the CLI?
- Duration estimate calibration: a fixed MB/s, or learned from the user's own measurements?
- Is the Squawk adapter permanent for Postgres, or a full move to libpg_query?
