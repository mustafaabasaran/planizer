# Planizer

**Validate and explain SQL changes before they run.**

[![CI](https://github.com/mustafaabasaran/planizer/actions/workflows/ci.yml/badge.svg)](https://github.com/mustafaabasaran/planizer/actions/workflows/ci.yml)
[![Catalog verification](https://github.com/mustafaabasaran/planizer/actions/workflows/catalog-verification.yml/badge.svg)](https://github.com/mustafaabasaran/planizer/actions/workflows/catalog-verification.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Planizer is a deterministic static-analysis linter for **SQL Server (MSSQL) / T-SQL migrations**.
It analyzes SQL migration scripts — DDL, DML, whole migration folders — and answers the
questions every review asks before approving a change for production: *Does it lock the table?
For how long? Does it rewrite data or just metadata? Can it be rolled back? Will it even run on
our edition?*

Every finding has the same shape: **rule id + severity (Info/Warning/Critical/Blocker) +
location (line:column) + one-sentence reason + suggested fix (as SQL where possible) + the
version/edition assumption it was produced under**. The report serves two jobs at once: a
**validator** (machine decision — it fails CI via the exit code) and an **explainer**
(human-readable reasoning for the reviewer).

## Why

Code generation — especially AI-assisted — has exploded; validation and understanding are the
bottleneck. For SQL in particular, tests passing says nothing about whether a migration locks a
production table, rewrites terabytes, or destroys data irrecoverably. Postgres has
[Squawk](https://github.com/sbdchd/squawk) as a de-facto standard for this. **SQL Server has
nothing equivalent**: existing T-SQL linters stop at style and anti-patterns, and plan
analyzers only look at queries. DDL/migration safety on MSSQL — lock levels, rewrite vs
metadata-only, reversibility, edition traps — is the gap Planizer fills: **what Squawk is for
Postgres, Planizer aims to be for SQL Server.**

Design decisions:

- **No LLM.** Every rule is deterministic: Microsoft's own T-SQL parser (ScriptDom) + a
  behavior catalog (CSV) mapping `operation × version × edition → lock, data movement,
  reversibility`.
- **Rules never stay silent.** When offline analysis cannot decide (e.g. whether an
  `ALTER COLUMN` narrows), the finding is reported and marked `inconclusive` instead of being
  dropped.
- **Worst-case defaults.** Without flags, Planizer assumes SQL Server 2019 **Standard**
  edition — the edition where the expensive surprises live. Test boxes usually run Developer
  (= Enterprise behavior), which hides them.

## What you get

- **53 rules in six categories** — locking/blocking, rewrite vs metadata-only, reversibility
  (with an opt-in auto-generated rollback script, `--rollback`), failure risk (idempotency, batch/`GO` compile
  errors, index-key and identifier limits, features missing on the target version), and
  transaction & script hygiene (`XACT_ABORT`, `BEGIN`/`COMMIT` balance, `TRY`/`CATCH`,
  `SET` options, `USE` and cross-database coupling). See [Rules](#rules).
- **Whole-script analysis** — statements nested in `IF`/`BEGIN…END`/`WHILE`/`TRY…CATCH` are
  analyzed like top-level ones, with their batch and control-flow context.
- **Four outputs** — `text` for the terminal, `json` for machines, `markdown` for PR comments,
  `sarif` for GitHub code scanning and IDE problem panes; exit codes for CI.
- **Suppressions and config** — `-- planizer:ignore RULE reason` on a statement,
  `.planizer.json` per directory for version, edition and per-rule overrides.
- **A composite GitHub Action** — `uses: mustafaabasaran/planizer@v0.1.1` builds the CLI from source,
  fails the job on findings above a threshold and writes SARIF for `upload-sarif`.

## Install

**Prebuilt binary — no .NET required.** Every release ships a self-contained Native AOT binary
per platform (Linux x64/arm64, Windows x64, macOS arm64) plus a `SHA256SUMS.txt`, under
[Releases](https://github.com/mustafaabasaran/planizer/releases):

```sh
curl -fsSLO https://github.com/mustafaabasaran/planizer/releases/download/v0.1.1/planizer-v0.1.1-linux-x64.tar.gz
tar xzf planizer-v0.1.1-linux-x64.tar.gz
./planizer analyze migrations/
```

**Docker — no toolchain at all.** Multi-arch (amd64/arm64) image on ghcr.io, tagged `latest` and
per release; mount the directory to analyze on `/work`:

```sh
docker run --rm -v "$PWD:/work" ghcr.io/mustafaabasaran/planizer:latest analyze .
```

**dotnet tool.** Planizer is a .NET 10 CLI packaged as a `dotnet tool`, built against the current
LTS runtime and rolling forward to newer majors (`RollForward=LatestMajor`), so it also runs on
whatever later .NET is already installed on a build agent. It is **not yet published to
NuGet.org**; until then, build and install it from source:

```sh
git clone <this repo> && cd planizer
dotnet pack src/Planizer.Cli -c Release
dotnet tool install --global --add-source src/Planizer.Cli/nupkg Planizer
```

Or run it straight from the repo without installing:

```sh
dotnet run --project src/Planizer.Cli -- analyze <files>
```

## Usage

```sh
planizer analyze <file-or-directory>... [options]
```

Directories are searched recursively for `*.sql` (sorted by name). Options:

| Option | Values | Default |
|---|---|---|
| `--dialect` | `mssql` | `mssql` |
| `--target-version` | `2014`, `2016`, `2017`, `2019`, `2022`, `azure` | `2019` |
| `--edition` | `enterprise`, `standard`, `express`, `azure`, `developer` | `standard` |
| `--output` | `text`, `json`, `markdown`, `sarif` | `text` |
| `--sarif-file` | path | — (also write SARIF 2.1.0 to this file, in addition to `--output`) |
| `--fail-on` | `info`, `warning`, `critical`, `blocker` | `critical` |
| `--config` | path to a config file | nearest `.planizer.json` to the input, then cwd |
| `--rollback` | flag | off — opt-in rollback analysis: reverse script, MSSQL-REV-002, rollback status (or `"rollback": true` in `.planizer.json`) |
| `--no-progress` | flag | progress indicator on stderr is on when stderr is a terminal, off otherwise |
| `--timing` | flag | off — appends parse/rules/total and the slowest rules to text and markdown output |

`developer` is mapped to Enterprise internally (identical behavior). And:

```sh
planizer rules        # list every rule: id, default severity, title
```

### Text output (default)

```
$ planizer analyze migration.sql --edition standard
== migration.sql ==
  migration.sql:1:1  ALTER TABLE dbo.Orders ADD Status tinyint NOT NULL DEFAULT 0;
    Warning MSSQL-LOCK-001 ADD COLUMN Status takes a schema-modification (Sch-M) lock on dbo.Orders, held for the duration of the operation; all reads and writes are blocked.
    Critical MSSQL-RW-002 Adding NOT NULL column Status with a default to dbo.Orders rewrites the entire table on Standard edition.
      fix: Run during low traffic, or use expand/contract: add the column as nullable, backfill in batches, then ALTER TABLE dbo.Orders ALTER COLUMN Status ... NOT NULL;
    Info MSSQL-RW-016 Cannot verify the current row width of dbo.Orders offline; adding Status (tinyint) grows each row by 1 byte toward the 8060-byte in-row limit. [inconclusive]

Summary
  Assumption: SQL Server 2019, Standard edition, offline mode
  Statements: 1 total, 1 DDL, 1 taking Sch-M locks, 0 irreversible, 0 unanalyzable
  Findings:   3 total (0 Blocker, 1 Critical, 1 Warning, 1 Info), 0 suppressed
```

Rollback analysis is opt-in: `--rollback` adds a `Rollback: complete|incomplete (N reverse
statement(s) generated)` line, reports statements without an automatic inverse (MSSQL-REV-002) and
puts the generated reverse script into the JSON (`summary.rollbackScript`) and markdown output.
Without it `rollbackComplete` is `null` and the data-loss rule MSSQL-REV-001 still runs.

Findings are grouped per statement: a `file:line:column` header shows the statement itself, and
the rules that fired on it follow. Colors are ANSI; set the `NO_COLOR` environment variable to
disable them.

While a run is in progress a one-line indicator (`planizer | parsing 12/47  file.sql`, then
`rules 23/53  MSSQL-TRAN-003`) is drawn on **stderr** and erased before the report is written, so
stdout stays clean for `--output json|sarif|markdown` pipelines. It only appears when stderr is an
interactive terminal; `--no-progress` turns it off explicitly. `--timing` adds a block with parse /
rules / total time and the five slowest rules (JSON output always carries the full per-rule
`timing` object) — handy for spotting which rule makes a large directory scan slow.

**What counts as a statement.** Planizer analyzes every statement that runs at deploy time: the
top-level statements of each `GO` batch *and* the statements nested in `IF`/`ELSE`, `BEGIN…END`,
`WHILE` and `TRY…CATCH` bodies, recursively. An `ALTER TABLE` guarded by
`IF COL_LENGTH(…) IS NULL` is therefore reported like a bare one (the guard is visible to the
idempotency rules, not to the lock rules — the lock is taken either way). The `Statements:` line
of the summary counts nested statements and the control-flow wrappers themselves; wrappers are
not DDL. Module bodies — the inside of `CREATE`/`ALTER PROCEDURE`, `FUNCTION`, `TRIGGER`, `VIEW`
— are definitions, not migration actions, and are not analyzed.

### JSON output

`--output json` writes the full report — camelCase, enums as strings — for machine consumers:

```json
{
  "toolVersion": "0.1.0",
  "dialect": "MsSql",
  "targetVersion": "2019",
  "edition": "Standard",
  "mode": "Offline",
  "files": ["migration.sql"],
  "findings": [
    {
      "ruleId": "MSSQL-RW-002",
      "severity": "Critical",
      "message": "Adding NOT NULL column Status with a default to dbo.Orders rewrites the entire table on Standard edition.",
      "fix": "Run during low traffic, or use expand/contract: ...",
      "location": { "file": "migration.sql", "line": 1, "column": 1 },
      "statementSummary": "ALTER TABLE dbo.Orders ADD Status int NOT NULL DEFAULT 0;",
      "assumption": "SQL Server 2019, Standard edition, offline mode",
      "inconclusive": false,
      "suppressed": false,
      "suppressReason": null
    }
  ],
  "summary": { "...": "statement counts, rollback script, rollbackComplete" },
  "suppressedCount": 0
}
```

### Markdown output

`--output markdown` renders a report made to be pasted into a PR comment: findings as a table
sorted by severity, the generated rollback script in a collapsible `<details>` block, and a
one-line summary:

```markdown
# Planizer report

**Files:** `migration.sql`
**Assumption:** SQL Server 2019, Standard edition, offline mode

| Severity | Rule | Location | Message | Fix |
| --- | --- | --- | --- | --- |
| Critical | MSSQL-RW-002 | `migration.sql:1:1` | Adding NOT NULL column Status ... | Run during low traffic, or ... |

**Summary:** 1 statements (1 DDL, 1 Sch-M, 0 irreversible, 0 unanalyzable) · findings: 0 Blocker, 1 Critical, 2 Warning, 1 Info (0 suppressed) · rollback incomplete
```

### SARIF output

`--output sarif` writes a [SARIF 2.1.0](https://sarifweb.azurewebsites.net/) report for GitHub
code scanning, Azure DevOps and IDE problem panes; `--sarif-file planizer.sarif` writes the same
document to a file **in addition to** whatever `--output` prints, so one run can log text and
upload SARIF. Every rule is listed in `tool.driver.rules` (with `properties.docs` pointing at
its page under `docs/rules/`); each finding is a `result` with `level` `note` / `warning` /
`error` for Info / Warning / Critical+Blocker, the fix appended to the message, the file as a
`%SRCROOT%`-relative URI (run from the repository root), an `inSource` suppression when a
`planizer:ignore` directive applied, and the exact Planizer severity, assumption and statement
in `properties`. `runs[0].originalUriBaseIds["%SRCROOT%"]` records the working directory of the
machine that produced the report (that is what the relative URIs are relative to); code scanning
resolves `%SRCROOT%` to the checkout root and ignores the recorded path, so the committed
`samples/planizer.sarif` normalizes that line to `file:///workspace/planizer/`; regenerate it from
the repository root and expect that one line to differ per machine. See `samples/planizer.sarif` for
the report of the `samples/` directory and [ADR-0002](docs/adr/0002-sarif-handwritten.md) for why
it is written by hand.

```sh
planizer analyze migrations/ --output text --sarif-file planizer.sarif
# then: github/codeql-action/upload-sarif@v3 with sarif_file: planizer.sarif
# (or let the composite action do both — see "Use in GitHub Actions" below)
```

## Suppressing findings

Put a `planizer:ignore` comment on the statement's first line or the line directly above it:

```sql
-- planizer:ignore MSSQL-LOCK-009 nightly job, table is not in use at 03:00
DELETE FROM dbo.SessionCache;
```

Multiple rule ids are comma-separated; everything after the ids is a free-text reason. A
directive on an `IF`, `BEGIN…END`, `WHILE` or `TRY…CATCH` block covers every statement nested
in it. Suppressed findings stay in the report — marked `[suppressed: <reason>]` in text and
markdown output, with the reason also in the JSON report's `suppressReason` — but do not
count toward the exit code.

## Configuration

Without `--config`, Planizer uses the nearest `.planizer.json` to the analyzed files: the
first input path's own directory, then each ancestor directory upward, then the current
working directory as a fallback. So `planizer analyze samples/001.sql` from the repo root
honors `samples/.planizer.json`. CLI options override config-file values.

```json
{
  "dialect": "mssql",
  "targetVersion": "2019",
  "edition": "standard",
  "failOn": "critical",
  "rules": {
    "MSSQL-RW-016": { "enabled": false },
    "MSSQL-LOCK-009": { "severity": "Critical" }
  }
}
```

Per rule: `enabled: false` drops the rule entirely; `severity` overrides its default. Unknown
rule ids are not an error.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | No unsuppressed finding at or above the `--fail-on` threshold |
| `1` | At least one unsuppressed finding at/above `--fail-on` (parse errors count: `MSSQL-PARSE-001` is a Blocker) |
| `2` | Tool error — missing file, invalid argument, bad config |

Severity order: `Info < Warning < Critical < Blocker`.

## Use in GitHub Actions

The repository root is a **composite GitHub Action** (`action.yml`). It builds the CLI from the
action's own checkout — no NuGet publication needed — analyzes the given path, prints the text
report to the job log, writes a SARIF file and fails the step according to `fail-on`:

```yaml
name: SQL migration check
on:
  pull_request:
    paths: ['migrations/**/*.sql']

permissions:
  contents: read
  security-events: write   # only needed for the SARIF upload below

jobs:
  planizer:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Planizer
        id: planizer
        uses: mustafaabasaran/planizer@v0.1.1   # pin a tag or commit SHA in real pipelines
        with:
          path: migrations
          target-version: '2019'
          edition: standard
          fail-on: critical
          sarif-file: planizer.sarif

      - name: Upload SARIF to code scanning
        if: ${{ !cancelled() && steps.planizer.outputs.sarif-file != '' }}
        uses: github/codeql-action/upload-sarif@v3
        with:
          sarif_file: ${{ steps.planizer.outputs.sarif-file }}
          category: planizer
```

Inputs:

| Input | Default | Meaning |
|---|---|---|
| `path` | `.` | File or directory (searched recursively for `*.sql`); several paths may be space-separated, or one per line (use the multi-line form when a path contains a space) |
| `target-version` | `2019` | `2014`, `2016`, `2017`, `2019`, `2022`, `azure` |
| `edition` | `standard` | `enterprise`, `standard`, `express`, `azure`, `developer` |
| `fail-on` | `critical` | `info`, `warning`, `critical`, `blocker` — lowest severity that fails the step |
| `config` | — | Path to a `.planizer.json`; empty discovers the nearest one |
| `sarif-file` | `planizer.sarif` | Where the SARIF 2.1.0 report is written, relative to the workspace |
| `dotnet-version` | `10.0.x` | .NET SDK used to build and run the CLI |

`target-version`, `edition` and `fail-on` are passed to the CLI as options and therefore
**override** the values in `.planizer.json`; set an input to `''` to defer to the config file
instead.

Outputs: `sarif-file` (the path that was written) and `exit-code` (`0` / `1` / `2`, see
[Exit codes](#exit-codes)). The step itself exits with the analyzer's exit code, so a finding
at or above `fail-on` fails the job; add `continue-on-error: true` to the step if you prefer to
read `exit-code` and decide yourself.

**Code scanning.** After `upload-sarif`, each finding becomes a code scanning alert under the
repository's **Security → Code scanning** tab, and on pull requests it is shown as an inline
annotation at the statement's `file:line` in the *Files changed* view — rule id, level
(`note` / `warning` / `error`) and the message with the suggested fix. Alerts close
automatically once the statement is fixed or the file is gone. The upload needs
`security-events: write` and code scanning enabled (free for public repositories, GitHub
Advanced Security for private ones); add `continue-on-error: true` to the upload step when that
is not guaranteed, as this repository's own CI does for its `samples/` self-check.

Once Planizer is published to NuGet.org, a plain `dotnet tool install --global Planizer`
followed by `planizer analyze migrations --sarif-file planizer.sarif` in a `run:` step is an
equivalent, build-free alternative to the action.

## Rules

53 rules in six categories. Each links to a page with what it checks, why it matters, a
real example, the fix, and its version/edition assumptions.

### Parse & dynamic SQL

| Rule | Severity | Title |
|---|---|---|
| [MSSQL-PARSE-001](docs/rules/MSSQL-PARSE-001.md) | Blocker | SQL script does not parse (produced by the analyzer itself) |
| [MSSQL-DYN-001](docs/rules/MSSQL-DYN-001.md) | Warning | Dynamic SQL cannot be analyzed statically |

### Locking / blocking

| Rule | Severity | Title |
|---|---|---|
| [MSSQL-LOCK-001](docs/rules/MSSQL-LOCK-001.md) | Warning | Schema modification lock (Sch-M) blocks all access to the table |
| [MSSQL-LOCK-002](docs/rules/MSSQL-LOCK-002.md) | Warning | Offline index build blocks access to the table |
| [MSSQL-LOCK-003](docs/rules/MSSQL-LOCK-003.md) | Blocker | ONLINE = ON is not available on this edition |
| [MSSQL-LOCK-004](docs/rules/MSSQL-LOCK-004.md) | Info | Online index operation without WAIT_AT_LOW_PRIORITY |
| [MSSQL-LOCK-005](docs/rules/MSSQL-LOCK-005.md) | Info | Online index operation without RESUMABLE |
| [MSSQL-LOCK-006](docs/rules/MSSQL-LOCK-006.md) | Warning | Offline index rebuild blocks all access to the table |
| [MSSQL-LOCK-007](docs/rules/MSSQL-LOCK-007.md) | Critical | Multiple Sch-M locks held until COMMIT in one transaction |
| [MSSQL-LOCK-008](docs/rules/MSSQL-LOCK-008.md) | Warning | Sch-M locks on multiple tables in one transaction (deadlock potential) |
| [MSSQL-LOCK-009](docs/rules/MSSQL-LOCK-009.md) | Warning | Unbounded UPDATE/DELETE escalates to a table lock |
| [MSSQL-LOCK-010](docs/rules/MSSQL-LOCK-010.md) | Warning | Transactional DDL without SET LOCK_TIMEOUT |

### Rewrite vs metadata-only

| Rule | Severity | Title |
|---|---|---|
| [MSSQL-RW-001](docs/rules/MSSQL-RW-001.md) | Info | Adding a nullable column without a default is metadata-only |
| [MSSQL-RW-002](docs/rules/MSSQL-RW-002.md) | Critical | Adding a NOT NULL column with a default may rewrite the entire table |
| [MSSQL-RW-003](docs/rules/MSSQL-RW-003.md) | Blocker | Adding a NOT NULL column without a default fails on a non-empty table |
| [MSSQL-RW-004](docs/rules/MSSQL-RW-004.md) | Critical | Altering a column to a MAX type rewrites the column data |
| [MSSQL-RW-005](docs/rules/MSSQL-RW-005.md) | Critical | Column type change may rewrite the table depending on the current type |
| [MSSQL-RW-006](docs/rules/MSSQL-RW-006.md) | Critical | Narrowing a column loses data and risks truncation failures |
| [MSSQL-RW-007](docs/rules/MSSQL-RW-007.md) | Critical | Altering a column to NOT NULL scans the whole table and fails on NULLs |
| [MSSQL-RW-008](docs/rules/MSSQL-RW-008.md) | Info | Altering a column to NULL is metadata-only |
| [MSSQL-RW-009](docs/rules/MSSQL-RW-009.md) | Critical | Changing a column collation rewrites the column and needs dependent indexes dropped |
| [MSSQL-RW-010](docs/rules/MSSQL-RW-010.md) | Warning | Dropping a column does not reclaim its space |
| [MSSQL-RW-011](docs/rules/MSSQL-RW-011.md) | Warning | Adding a PERSISTED computed column scans and writes the whole table |
| [MSSQL-RW-012](docs/rules/MSSQL-RW-012.md) | Critical | Changing DATA_COMPRESSION rewrites the whole table or index |
| [MSSQL-RW-013](docs/rules/MSSQL-RW-013.md) | Critical | Creating or dropping a clustered index rewrites the entire table |
| [MSSQL-RW-014](docs/rules/MSSQL-RW-014.md) | Warning | Adding a CHECK/FOREIGN KEY constraint scans all existing rows |
| [MSSQL-RW-015](docs/rules/MSSQL-RW-015.md) | Warning | Adding a PRIMARY KEY/UNIQUE constraint builds an index with a uniqueness scan |
| [MSSQL-RW-016](docs/rules/MSSQL-RW-016.md) | Warning | Row width against the 8060-byte in-row limit |

### Reversibility

| Rule | Severity | Title |
|---|---|---|
| [MSSQL-REV-001](docs/rules/MSSQL-REV-001.md) | Critical | Statement is irreversible; data cannot be restored |
| [MSSQL-REV-002](docs/rules/MSSQL-REV-002.md) | Warning (DDL) / Info (DML, per file) | No automatic rollback statement could be generated — only with `--rollback` |
| [MSSQL-REV-003](docs/rules/MSSQL-REV-003.md) | Warning | sp_rename leaves dependent objects pointing at the old name |
| [MSSQL-REV-004](docs/rules/MSSQL-REV-004.md) | Warning | TRUNCATE TABLE: rollback window and FK restrictions |
| [MSSQL-REV-005](docs/rules/MSSQL-REV-005.md) | Warning | SET IDENTITY_INSERT ON is never turned OFF |

### Failure risk (will the script error out in production?)

| Rule | Severity | Title |
|---|---|---|
| [MSSQL-IDEM-001](docs/rules/MSSQL-IDEM-001.md) | Warning | CREATE without an existence check is not re-runnable |
| [MSSQL-IDEM-002](docs/rules/MSSQL-IDEM-002.md) | Warning | ALTER TABLE ADD/DROP without an existence check is not re-runnable |
| [MSSQL-IDEM-003](docs/rules/MSSQL-IDEM-003.md) | Warning | DROP without IF EXISTS is not re-runnable |
| [MSSQL-BATCH-001](docs/rules/MSSQL-BATCH-001.md) | Blocker | Column added in the same batch is referenced before GO |
| [MSSQL-BATCH-002](docs/rules/MSSQL-BATCH-002.md) | Blocker | Variable declared in an earlier batch is used after GO |
| [MSSQL-LIT-001](docs/rules/MSSQL-LIT-001.md) | Warning (per file) | Non-ASCII string literal without the N prefix |
| [MSSQL-LIM-001](docs/rules/MSSQL-LIM-001.md) | Blocker / Critical | Index key exceeds the column-count or byte-size limit |
| [MSSQL-LIM-002](docs/rules/MSSQL-LIM-002.md) | Blocker | Identifier longer than SQL Server allows |
| [MSSQL-VER-001](docs/rules/MSSQL-VER-001.md) | Blocker (Warning for 2016 SP1 features) | Feature not available on the target SQL Server version |

There is no separate `MSSQL-BATCH-003` ("CREATE PROCEDURE/VIEW/FUNCTION/TRIGGER/SCHEMA must be
alone in its batch"): ScriptDom rejects a module definition that is not the first statement of
its batch at parse time (error 46010, *Incorrect syntax near 'CREATE'*), so MSSQL-PARSE-001
already reports it.

### Transaction & script hygiene

| Rule | Severity | Title |
|---|---|---|
| [MSSQL-TRAN-001](docs/rules/MSSQL-TRAN-001.md) | Warning (per file) | Explicit transaction without SET XACT_ABORT ON |
| [MSSQL-TRAN-002](docs/rules/MSSQL-TRAN-002.md) | Critical | Unbalanced BEGIN TRAN / COMMIT / ROLLBACK |
| [MSSQL-TRAN-003](docs/rules/MSSQL-TRAN-003.md) | Warning | Transaction spans GO batches |
| [MSSQL-TRAN-004](docs/rules/MSSQL-TRAN-004.md) | Critical | BEGIN TRAN inside TRY without ROLLBACK in CATCH |
| [MSSQL-TRAN-005](docs/rules/MSSQL-TRAN-005.md) | Warning | CATCH block swallows the error |
| [MSSQL-TRAN-006](docs/rules/MSSQL-TRAN-006.md) | Info | Long explicit transaction |
| [MSSQL-SET-001](docs/rules/MSSQL-SET-001.md) | Blocker / Warning | Filtered index / PERSISTED computed column needs QUOTED_IDENTIFIER and ANSI_NULLS ON |
| [MSSQL-SET-002](docs/rules/MSSQL-SET-002.md) | Info (per file) | Many DML statements without SET NOCOUNT ON |
| [MSSQL-ENV-001](docs/rules/MSSQL-ENV-001.md) | Info | USE [database] overrides the migration runner's target database |
| [MSSQL-ENV-002](docs/rules/MSSQL-ENV-002.md) | Warning / Info (per file) | Linked-server or cross-database reference ties the script to one environment |
| [MSSQL-ENV-003](docs/rules/MSSQL-ENV-003.md) | Info (per file) | Long-running DDL with no progress messages |

## How it compares

| Tool | Focus | Where Planizer differs |
|---|---|---|
| [Squawk](https://github.com/sbdchd/squawk) | Postgres migration linting | Postgres only; Planizer covers SQL Server first (Postgres is on the [roadmap](docs/ROADMAP.md) via a Squawk adapter) |
| [tsqllint](https://github.com/tsqllint/tsqllint) | T-SQL style and anti-patterns | No lock/rewrite/reversibility model; Planizer reasons about what the DDL does to a production table |
| [ErikEJ's SQL Server analyzers](https://github.com/ErikEJ/SqlServer.Rules) (140+ rules) | T-SQL code quality | Same: code-level rules, not migration safety (Sch-M windows, rewrite vs metadata-only, edition traps) |
| PerformanceStudio | Execution-plan analysis | Query plans, not DDL; a plan-analysis adapter is planned for a later phase |
| [Atlas](https://atlasgo.io) | Schema-as-code and migration linting | Commercial, pulls you into its own migration format; Planizer analyzes plain `.sql`, no lock-in |
| [strong_migrations](https://github.com/ankane/strong_migrations) | Rails/Postgres migration safety | Ruby/Postgres ecosystem; Planizer is CI-first and dialect-adaptered |

Uniquely, Planizer's claims are **empirically verified**: a [CI job](.github/workflows/catalog-verification.yml)
runs every behavior-catalog row against real SQL Server containers (Developer *and* Express) and
fails on any contradiction between the docs and the engine.

## Using with AI agents

The `json` output is stable and machine-readable (findings + per-rule timing + summary), the exit
code is threshold-driven, and every rule has a documentation page an agent can quote
(`docs/rules/<RULE-ID>.md`). A machine-readable project summary lives in [`llms.txt`](llms.txt).
An MCP server (`analyze_migration`, `explain_finding`, `get_lock_profile`) is planned — see the
[roadmap](docs/ROADMAP.md).

## Roadmap

Done: **Phase 1 — MSSQL DDL safety, offline** (locking, rewrite vs metadata-only, reversibility)
and **Phase 1.5 — failure risk and script hygiene** (idempotency, batch/GO, limits, version
compatibility, transactions, SET options, environment coupling; SARIF output; GitHub Action).
Next: schema/statistics snapshots ("estimated X sec, Y MB log"), DML & execution-plan analysis
with an MCP server, then Postgres via a Squawk adapter. Details and exit criteria:
[docs/ROADMAP.md](docs/ROADMAP.md).

## Development

```sh
dotnet build -c Release      # warnings are errors
dotnet test  -c Release
```

Every rule has a page under `docs/rules/` and at least one triggering and one clean fixture under
`tests/Planizer.Tests/Fixtures/<RULE-ID>/` (`-- expect:` / `-- expect-none:` directives, see
`FixtureTests`). Before changing a rule's shape, run it over real migrations:
`scripts/corpus-scan.sh DIR [DIR…]` builds the Release CLI, analyzes each directory, and prints a
rule × severity count plus a few sample statements per rule — the tool behind the false-positive
rounds recorded in `CLAUDE.md` (the header of the script documents the `PLANIZER_*` environment
knobs).

The claims in the DDL behavior catalog (`src/Planizer.MsSql/Catalog/mssql-ddl-behavior.csv`) are
verified against a real SQL Server in CI: the
[catalog verification workflow](.github/workflows/catalog-verification.yml) runs the probe suite
in `tests/Planizer.CatalogVerification.Tests` against Dockerized Developer and Express instances
and fails only when a measurement contradicts a catalog row. These tests never run locally —
without `PLANIZER_CATALOG_VERIFY=1` (set only in that workflow) every probe test skips and no
container is started.

## Contributing

The most valuable contribution is a **false-positive report**: the issue form asks for the
minimal SQL, and that SQL becomes a regression fixture. Rule proposals and code are just as
welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for the anatomy of a rule (catalog row →
fixtures → rule class → registry line → doc page) and the project's ground rules. Questions and
ideas go to [Discussions](https://github.com/mustafaabasaran/planizer/discussions).

## License

MIT.
