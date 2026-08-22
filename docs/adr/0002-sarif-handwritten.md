# ADR-0002: Write SARIF 2.1.0 by hand with System.Text.Json instead of the Sarif SDK

- Status: accepted
- Date: 2026-08-21
- Decision source: Phase 1.5 implementation plan (internal)

## Context

GitHub code scanning, Azure DevOps and most IDE "problems" panes ingest SARIF 2.1.0. Planizer's
findings already have every field a SARIF `result` needs (rule id, severity, location, message,
fix, suppression), so the output is a projection of the existing `Report`, not new analysis.
Microsoft ships an official object model (`Sarif.Sdk`, `Microsoft.CodeAnalysis.Sarif`) that the
Phase 0 technology table pencilled in; the question was whether to take that dependency or emit the
JSON directly.

What Planizer needs from SARIF is small and fixed: one run, `tool.driver` with a `rules[]`
array, one `result` per finding with `ruleIndex`, `level`, `message`, a single
`physicalLocation` (root-relative URI + `region`), an `inSource` suppression when a
`planizer:ignore` directive applied, and a `properties` bag carrying the Planizer-specific
fields (severity name, `inconclusive`, `assumption`, statement summary).

## Options considered

- **Hand-written `Utf8JsonWriter`, ~200 lines, covered by writer tests** — no new package,
  AOT-friendly, output shape fully under our control. Chosen.
- `Sarif.Sdk` NuGet — complete object model and a validator, but it pulls in Newtonsoft.Json
  (the CLI otherwise uses only System.Text.Json), is heavy for a single-binary Native AOT CLI
  (reflection-based serialization, large dependency closure), and its API surface is far bigger
  than the dozen properties used here.
- `System.Text.Json` serialization of hand-made POCO records — same dependency profile as the
  chosen option, but the nested SARIF shape (`message.text`, `shortDescription.text`,
  `physicalLocation.artifactLocation`) needs a record per level and custom naming for `$schema`;
  the writer is shorter and the JSON it produces is explicit in one place.

## Decision

`Planizer.Cli/Output/SarifReportWriter` writes SARIF 2.1.0 directly with `Utf8JsonWriter`.
The emitted subset is: `$schema`, `version`; `runs[0].tool.driver` {`name`,
`semanticVersion`, `informationUri`, `rules[]` {`id`, `name`, `shortDescription.text`,
`defaultConfiguration.level`, `properties.docs`}}; `runs[0].invocations[0].executionSuccessful`;
`runs[0].originalUriBaseIds["%SRCROOT%"]` = the working directory; `runs[0].results[]` {`ruleId`,
`ruleIndex`, `level`, `message.text` (message plus `\n\nFix: …` when a fix exists),
`locations[0].physicalLocation` {`artifactLocation` {`uri` root-relative with `/` separators,
`uriBaseId` `%SRCROOT%`}, `region` {`startLine`, `startColumn`}}, `suppressions[]` {`kind`
`inSource`, `justification`} when suppressed, `properties` {`severity`, `inconclusive`,
`assumption`, `statement`}}.

Severity maps Info→`note`, Warning→`warning`, Critical and Blocker→`error` (SARIF has no
fourth level; the exact Planizer severity stays in `properties.severity`). Every MSSQL rule —
including the analyzer-produced MSSQL-PARSE-001 — is listed in `rules[]` so `ruleIndex` always
resolves, and GitHub can show rule titles for results that did not fire in this run.

The CLI exposes it two ways: `--output sarif` (stdout) and `--sarif-file <path>` (written in
addition to whatever `--output` selects, so a job can print text for the log and upload SARIF).

## Consequences

- No Newtonsoft.Json or Sarif SDK in the dependency graph; Native AOT stays on the table.
- Conformance is guarded by `SarifWriterTests` (mandatory fields, level mapping, suppressions,
  URI shape) and by the committed `samples/planizer.sarif`, which CI will upload to code
  scanning (Task 7). There is no in-process schema validator; if the subset grows, validate the
  sample against `sarif-2.1.0.json` in CI rather than adding the SDK.
- Adding a SARIF feature (e.g. `fixes[]` with replacements, `relatedLocations`, fingerprints)
  means extending the writer by hand. That is the accepted cost; the expected additions are few.
- Files outside the working directory cannot be expressed relative to `%SRCROOT%`; they are
  written as absolute `file://` URIs without `uriBaseId`, which code scanning will not map to
  repository files. Run the CLI from the repository root.
- `originalUriBaseIds["%SRCROOT%"]` is the producing machine's working directory, so the
  committed `samples/planizer.sarif` carries its author's checkout path and that one line churns
  when somebody else regenerates it. Accepted: consumers resolve `%SRCROOT%` themselves (GitHub
  uses the checkout root) and a neutral value would be a lie about where the files were read.
