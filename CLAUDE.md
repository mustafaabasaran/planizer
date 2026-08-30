# CLAUDE.md — Planizer

This file is the project context for Claude Code. Read it at the start of every session; decisions
live here and in ROADMAP.md.

## What the project is

**Planizer:** a tool that validates and explains SQL changes (migrations, DDL, DML, queries) before
they are executed. Target user: a developer / DBA / CI pipeline / AI agent who wants a fast,
reliable answer to "what will this do in production?" before approving a migration.

Every finding has the same shape:
**rule ID + severity (Info/Warning/Critical/Blocker) + location (line:column) + one-sentence rationale + suggested fix (corrected SQL where possible) + the version/edition assumption it was produced under.**

The report does two jobs: **validator** (machine decision, breaks CI) and **explainer** (human-readable
rationale). These are not separate features; they are two outputs of the same rule.

## Why it exists (problem statement)

- Code generation (especially with AI) has exploded; validation and understanding became the
  bottleneck. Especially in SQL: even when tests pass, there is no answer to "does this migration
  lock the table in prod, is it reversible, how long does it take".
- The questions asked in review are clear: **Is it fast? Does it use an index? Does it lock the
  table? Is it reversible? How long will it take on production data?**
- On the Postgres side this space is filled (Squawk is the de facto standard). **MSSQL has no
  Squawk equivalent:** tsqllint / ErikEJ T-SQL Analyzer stay at the style and anti-pattern level,
  PerformanceStudio only analyzes query plans, Atlas is commercial and pulls you into its own
  migration format. DDL/migration safety (lock level, online/offline, rewrite vs metadata-only,
  reversibility, edition traps) is not done by any standalone tool on MSSQL. **That is the gap.**

## Firm decisions

1. **No LLM.** All rules are deterministic: parser + rule table + system views. We are not walking
   into the irony of having AI validate the SQL that AI produced. An LLM may later be considered as
   an optional, separate layer on top of the report; never in the core.
2. **MSSQL first, then Postgres.** The user's primary need is MSSQL. Postgres is Phase 4.
3. **Order: DDL → DML/plan analysis → Postgres.** Phases 1–2 are DDL safety; Phase 3 is
   PerformanceStudio's territory (DML and execution plan analysis, MCP server); Phase 4 is Postgres.
   Doing what PerformanceStudio does is also a **goal**, don't forget it; but first the DDL part that
   nobody does.
4. **Don't rewrite what exists.** Squawk for Postgres DDL and PerformanceStudio for MSSQL plan
   analysis are first wired in as adapters; nativizing comes later and with justification.
5. **One engine, three source modes:**
   - **offline:** SQL text only. The CI default, zero setup.
   - **snapshot:** SQL + a schema/statistics JSON previously captured with `planizer snapshot`.
     The main mode for teams that don't want CI connecting to prod.
   - **live:** SQL + a live connection. Estimated plan, Query Store, everything enabled.
   Rules are fed through the `ISchemaProvider` / `IStatsProvider` interfaces. When the data is
   missing, a rule **says "inconclusive" rather than staying silent.** The report records which
   mode each finding was produced in. Phase 1 does offline only, but the provider interfaces are in
   place from day one.
6. **The core is dialect-agnostic.** `Planizer.Core` holds the report model, `IRule`, the provider
   interfaces and config. MSSQL and Postgres are each an adapter. The rule abstraction must be able
   to consume both the ScriptDom AST and Squawk JSON.
7. **CLI first.** GUI, IDE extension and MCP server come later, on top of the same Core.
8. **Every rule has a test:** at least one triggering and one non-triggering `.sql` fixture.
9. **The rule table is the heart of the project:** `statement × version × edition → lock level,
   rewrite/metadata-only, reversibility, note`. Kept as CSV, loaded from code. Every row must be
   verified against a real SQL Server in Docker (Developer = Enterprise behavior, and Express).

## Technology

| Layer | Choice | Note |
|---|---|---|
| Language | C# / .NET 10 (LTS) | Single-binary CLI via Native AOT |
| MSSQL parser | Microsoft.SqlServer.TransactSql.ScriptDom | The only serious T-SQL parser; it decided the language choice |
| Postgres parser | Phase 4: Squawk adapter first, then a libpg_query binding | .NET binding status to be researched |
| DB access | Microsoft.Data.SqlClient, Npgsql | |
| CLI | System.CommandLine | |
| Output | System.Text.Json; SARIF 2.1.0 written by hand (no Sarif SDK, ADR-0002); Markdown | SARIF → GitHub code scanning |
| Test | xUnit + .sql fixtures; Testcontainers (SQL Server / Postgres) | |
| Distribution | dotnet tool, GitHub Releases binary, Docker image | |
| MCP | ModelContextProtocol C# SDK | Phase 3 |

## Project structure

```
src/
  Planizer.Core        report model, IRule, IAnalysisContext, ISchemaProvider, IStatsProvider, config
  Planizer.MsSql       ScriptDom adapter + MSSQL rules + rule tables (CSV)
  Planizer.Postgres    Squawk adapter (Phase 4)
  Planizer.Cli         the planizer command
tests/
  Planizer.Tests       rule tests + fixtures/
docs/
  RULES.md             rule engine scope (9 sections, S/SCH/STAT layers)
  ROADMAP.md           phases and exit criteria
```

## Rule layers (RULES.md summary)

Rules are tagged by the input they need: **(S)** SQL text only, **(SCH)** schema information,
**(STAT)** statistics.

- **(S)** SQL text only — section 1 parse/classification, 2 locking/blocking, 3 rewrite vs
  metadata-only, 4 reversibility, 5 failure risk, 6 transaction/script hygiene
- **(SCH)** schema information — section 7: object existence, heaps, row size, duplicate indexes,
  FK indexes, SCHEMABINDING, dependency tree, temporal/CDC/replication/columnstore/in-memory/partitioning
- **(STAT)** statistics — section 8: duration estimate, log/tempdb/disk, table activity, index
  usage, Query Store baseline
- **Common** — section 9: report model, output formats, exit codes, `-- planizer:ignore RULE_ID reason`,
  `.planizer.json` config, snapshot generator

Critical MSSQL knowledge (the core of section 3):
- ADD COLUMN nullable: metadata-only. NOT NULL + DEFAULT: metadata-only on Enterprise (if the
  default is a runtime constant); on Standard/Express the whole table is written. Runtime constant =
  an expression evaluated once per statement, regardless of determinism: GETDATE()/SYSDATETIME()
  keep the fast path; NEWID()/NEWSEQUENTIALID(), evaluated per row, break it on every edition.
- ALTER COLUMN: widening varchar is metadata; int→bigint, precision/scale, collation → rewrite;
  NULL→NOT NULL full scan; narrowing means data loss.
- DROP COLUMN is metadata-only but the space is not reclaimed.
- CREATE INDEX offline: nonclustered → S lock on the table, clustered → Sch-M. ONLINE=ON is
  Enterprise. RESUMABLE 2017+ (rebuild) / 2019+ (create). WAIT_AT_LOW_PRIORITY: ALTER INDEX REBUILD
  2014+, CREATE INDEX only 2022+.
- ADD CONSTRAINT CHECK/FK scans the data; WITH NOCHECK does not scan but the constraint stays untrusted.
- sp_rename does not update dependent procs/views (deferred name resolution).

## Phases (ROADMAP.md summary)

- **Phase 0 — Skeleton:** solution, Core interfaces, ScriptDom parse + DDL/DML
  classification, first rule `MSSQL-LOCK-001` (DDL that takes Sch-M), CLI `planizer analyze <file|dir>
  --dialect mssql --output json|text`, exit codes, xUnit, CI. Exit: a lock report for your own migration.
- **Phase 1 — MSSQL DDL offline:** rule table CSV, config (`--target-version`,
  `--edition`), section 2–3–4 rules, suppression, text/markdown output, script summary, dynamic SQL
  detection, README + rule pages, dotnet tool v0.1.
- **Phase 1.5:** sections 5–6, SARIF, GitHub Action.
- **Phase 2 — Schema/statistics:** `planizer snapshot`, provider implementations,
  sections 7–8, Testcontainers. Exit: "estimated X s, Y MB of log".
- **Phase 3 — DML and plan analysis:** 3a PerformanceStudio adapter (SHOWPLAN_XML →
  .sqlplan → PS CLI JSON → Planizer report); 3b native plan analysis (PS's 30 rules as the baseline);
  static DML rules (UPDATE/DELETE without WHERE, SELECT *, non-SARGable predicates, leading wildcard,
  NOT IN on nullable, batching suggestion — these need no plan and can be pulled into Phase 1); plan
  comparison; Query Store; **MCP server** (`analyze_migration`, `analyze_plan`, `explain_finding`,
  `get_lock_profile`); GUI optional and last.
- **Phase 4 — Postgres:** Squawk adapter → libpg_query; Postgres lock table;
  EXPLAIN JSON analysis; pg_catalog providers; automatic dialect detection.
- **Phase 5 — Ecosystem:** EF Core / Flyway / Liquibase / DbUp, VS Code LSP, SSMS/ADS extension,
  Azure SQL differences, custom rule API.

## Competing / neighboring tools (known state)

- **PerformanceStudio** (erikdarlingdata, MIT, C#): .sqlplan analyzer, 30 rules, CLI+GUI+SSMS
  extension+MCP. Query plans only; does not look at DDL. Will be wired in as an adapter in Phase 3;
  whether to consume its Core via NuGet or via the CLI is to be decided. Architectural model to
  follow: Core/CLI/GUI/MCP separation, dual JSON+text output, severity+rationale on every rule.
- **Squawk** (Postgres): linter + LSP + VSCode + GitHub App. Not worth rewriting for Postgres.
- **tsqllint, ErikEJ T-SQL Analyzer (140+ rules), Microsoft SQL Database Projects code analysis:**
  T-SQL style/anti-patterns; no schema awareness, nothing about locks or duration.
- **Atlas:** migration lint including SQL Server, but commercial and pulls you into its own format.
- **strong_migrations, migration-lint, pg-schema-diff:** Postgres/ORM focused.

## Out of scope (for now)

MySQL/MariaDB/Oracle/SQLite; automatic query rewriting; LLM-based explanation; schema diff /
migration generation.

## Open questions

- Verifying the edition-dependent behaviors in the rule table in Docker (Developer vs Express).
- PerformanceStudio integration: NuGet reference or via the CLI?
- Duration estimate calibration: a fixed MB/s, or learned from the user's own measurements?
- Is the Squawk adapter permanent for Postgres, or a full move to libpg_query?
- Is there a mature .NET binding for libpg_query?

## Working style

- Small, working, used thing first; expand later. Not "invent a framework" but "the tool I use on
  my own PRs".
- Every new rule: CSV/table row first, then test fixture, then code.
- Don't guess questionable MSSQL behavior; run it in Docker and measure.
- Files: decisions go to ROADMAP.md, rules to RULES.md; this file is only summary and context.

## Current status

**Phase 1 and Phase 1.5 are complete** (2026-08-21). 53 rules in six categories: PARSE/DYN, LOCK (10),
RW (16), REV (5), failure risk IDEM (3) / BATCH (2) / LIT (1) / LIM (2) / VER (1), transaction and
script hygiene TRAN (6) / SET (2) / ENV (3). Every rule has a `docs/rules/<ID>.md` page and a
triggering/non-triggering fixture. Infrastructure: DdlBehaviorCatalog CSV + `mssql-feature-versions.csv`
(feature → minimum version), CLI `analyze` + `rules` (text/json/markdown/sarif, `--sarif-file`),
suppression + `.planizer.json`, `samples/` + `samples/planizer.sarif`, composite `action.yml` + CI
self-check (`samples/` → `upload-sarif`). ADRs: 0001 (REV-002 DML summary per file), 0002
(SARIF written by hand, no Sarif SDK).

**Nested statements are now analyzed:** the parser flattens IF/ELSE, BEGIN…END, WHILE and TRY/CATCH
bodies in pre-order; every `SqlStatementInfo` carries its batch order, depth, parent and
IF/ELSE/TRY/CATCH/WHILE flags (`IdempotencyGuard` and `TransactionPaths` are built on top of this).
Module bodies (inside CREATE/ALTER PROC/FUNCTION/TRIGGER/VIEW) are not flattened — they are
definitions, not migration actions; only LIM-002, VER-001 and ENV-002 also look inside module bodies
(the error occurs at CREATE time). Syntax that fails in the target grammar but parses in a newer
grammar becomes VER-001 instead of PARSE-001, and analysis continues with the newer parse. BATCH-003
was not written: ScriptDom reports a module definition that is not first in its batch as a parse
error, so PARSE-001 covers it.

**Corpus (a private corpus of 24 production repositories, 8,507 migration files, zero parse errors;
tool `scripts/corpus-scan.sh DIR…` — runs the Release CLI and prints rule × severity counts plus an
example per rule; the invocation is documented in the script header):** Phase 1 FP round
288,580 → 136,801 findings (REV-002 DML summary reduced to a single Info per file with ADR-0001);
after Phase 1.5, 1.58 million statements including nested ones, 44,179 findings, no FP class in the
new families. Highlights: ENV-002 3,994 Info (placeholder linked-server names substituted at deploy
time) + 66 Warning (real linked servers); SET-002 3,770; TRAN-003 741 (EF idempotent
`BEGIN TRANSACTION; GO … COMMIT; GO`); BATCH-001 61 Blocker (real error 207); LIM-001 8 Critical
(e.g. a 1101 B clustered key); BATCH-002, IDEM-002, LIM-002, TRAN-002, TRAN-004, VER-001 zero.
Re-scan after the review-round fixes (2026-08-21): 43,982 findings (ENV-002 3,992 Info, SET-002
3,649 — only write statements are counted now), same distribution in the new families; verification
against the sources (none of the BATCH-001 hits has a GO between the ALTER and the use, the TRAN-003
count equals the number of BEGIN TRAN in the file, the IDEM/TRAN-005 examples really have no
guard/THROW) turned up no FP class. The e2e reviewer's three findings were applied: LIT-001 no longer
counts PRINT/RAISERROR/THROW message text (not data), TRAN-003 collapses to a single finding when a
file has more than 5 GO-crossing transactions (EF scripts), BATCH-001 adds a "fails on a fresh
environment, compiles on an incremental one" note for guarded ALTERs. `samples/004` (idempotent,
exit 0) and `samples/005` (BATCH-001/002 Blocker, LIT/ENV/IDEM/TRAN) e2e samples + `SamplesTests`
were added.

**Review round (2026-08-21, Phase 1.5 close-out):** 18 findings verified and fixed — ENV-002 double
counting in nested statements; TRAN-002 `label:` error path after a top-level RETURN, forward GOTO
and `WHILE @@TRANCOUNT > 0 ROLLBACK`; IDEM-001..003 exit-guard idiom (`IF OBJECT_ID(…) IS NOT NULL
RETURN;` protects what follows in the same batch); LIM-002 `##` global temp 128; TRAN-004 savepoint
ROLLBACK is not counted (3931); IDEM-002 unnamed CHECK/FK/UNIQUE does not fail on re-run, it produces
a duplicate constraint (PK 1779 / DEFAULT 1781 error); IDEM-003 `SELECT … INTO` staging; SET-002
counts only INSERT/UPDATE/DELETE/MERGE, "DONE message" rather than "round trip"; BATCH-001 derived
table / CTE / table variable / TVF alias is not a match. Documentation: TRAN-002 DbUp (no default
transaction), VER-001/PARSE-001 grammar ladder (TSql120…TSql180), LIM-001 plan line, SARIF
`originalUriBaseIds` is the cwd of the producing machine, action `path` can be split across lines.

**Next:**
1. **Phase 2 — snapshot → live:** `planizer snapshot`, `ISchemaProvider` / `IStatsProvider`
   implementations, section 7–8 rules, Testcontainers.
2. **Catalog Docker verification in CI — harness ready, first run pending:** trigger
   `.github/workflows/catalog-verification.yml` (workflow_dispatch) and review the per-edition
   verdicts; expect an Inconclusive fix round (measurement code was written without a server).
3. **NuGet.org release** (dotnet tool v0.1; the package and a local-install smoke test are ready, an
   API key is needed) — afterwards the "build from source" notes in the README/action get simpler.

**Progress indicator + `--timing` (2026-08-21):** the analyzer takes an `IProgress<AnalysisProgress>`
(file/rule/done), `Report.Timing` carries each rule's duration; the CLI draws a throttled single-line
spinner on stderr (TTY only; `--no-progress`), `--timing` prints the slowest rules. The first
measurement caught two quadratic spots (the linear scans in `StatementsInFile/InBatch` and
`Statements.First(Index==…)`) → on one 505-file repository (126k statements) rule time went
44.9 → 7.4 s, total 63.5 → ~34 s. The remaining cost is ScriptDom parsing (~19–26 s); that is the
next perf candidate. Corpus scans can now be profiled with `--timing`.

**Rollback analysis is opt-in (2026-08-21, ADR-0003):** unless `--rollback` / `"rollback": true` is
given, no reverse script is generated, REV-002 stays silent and the summary has no `Rollback:` line
(`rollbackComplete: null`). REV-001 (data loss) is always on. Rationale: the team works forward-fix;
rollback is practically never used.

**Catalog verification harness (2026-08-25):** `tests/Planizer.CatalogVerification.Tests` measures
every claim in `mssql-ddl-behavior.csv` against a real SQL Server — 30 probes (columns / indexes /
objects, merged from three parallel branches) built on measurement primitives (log-bytes delta for
metadata-only vs rewrite, session logical reads for full-scan validation, a two-connection blocking
profile for lock claims, expected-error assertions). Verdict per row: Verified / Contradicted /
Inconclusive; only Contradicted fails `dotnet test`. Runs ONLY in
`.github/workflows/catalog-verification.yml` (workflow_dispatch + weekly cron + PR paths filter on
the catalog CSV and the test project; Developer/Express matrix on ubuntu-latest, Testcontainers,
markdown report as artifact + step summary). Locally the `[VerifyFact]` tests always skip —
`PLANIZER_CATALOG_VERIFY=1` is set only in that workflow, never on a development machine. The first
real run (and the resulting fix round from its verdicts) has not happened yet.

**Catalog verified in CI (2026-08-25):** the first two live runs on GitHub Actions (SQL Server
2022 containers, Developer + Express) measured every behavior-catalog row. Run 1: 26/30 verified,
two genuine catches — `alter_column_collation` was wrongly cataloged as rewrite (the swap alone is
metadata-only; RW-009 is now Warning + inconclusive with the code-page condition) and the
evaluator's brief-lock expectation was wrong for concurrently sampled probes. Run 2 after the
fixes: Developer 30/30 Verified, Express 27/27 applicable rows Verified, zero Contradicted, zero
Inconclusive. The project's oldest open question (Docker validation of the catalog) is closed;
the weekly cron keeps it honest. Repo is live (private) at github.com/mustafaabasaran/planizer;
the public flip is a one-command decision.

**Public release (2026-08-28):** repo public, v0.1.0 release published, GitHub Marketplace listing
live (github.com/marketplace/actions/planizer), commit history rewritten once to the personal
e-mail before going public (no further history rewrites). NuGet.org publish still waits on an API
key.

**Distribution: Native AOT + Docker (2026-08-30):** the CLI is AOT-clean — rule discovery moved
from reflection (`Assembly.GetTypes`, which trimming silently reduced to 1 of 53 rules) to an
explicit `RuleRegistry` guarded by a reflection-comparison test; JSON report/config moved to
source-generated System.Text.Json (config properties became `set` because the generated
deserializer clobbers init-only defaults — that FP was caught by `ConfigTests`); Core/MsSql build
with `IsAotCompatible`, so new reflection is a compile error. ScriptDom itself trims clean with
zero warnings. Verified against a real osx-arm64 AOT build: 53 rules, text/markdown/sarif output
byte-identical to managed, ~16x faster startup. `release.yml` (AOT binaries for
linux-x64/linux-arm64/win-x64/osx-arm64 + SHA256SUMS attached on a `v*` tag) and `docker.yml`
(multi-arch ghcr.io image from native runners, chiseled-extra base) are both in place and
rehearsed green via `workflow_dispatch`. First real publish lands with the next tag — v0.1.0
predates the registry fix, so it gets no binaries. After the first image push, flip the ghcr
package to public once in the package settings.

