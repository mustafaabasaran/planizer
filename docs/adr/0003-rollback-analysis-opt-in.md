# ADR-0003: Rollback analysis is opt-in (`--rollback`)

- Status: accepted
- Date: 2026-08-21
- Decision source: maintainer decision in chat ("gate it behind --rollback; we practically never roll back")
- Supersedes in part: ADR-0001 (the per-file REV-002 summary now appears only with `--rollback`)

## Context

Planizer answers "can this be rolled back?" by trying to generate an inverse for every
state-changing statement: the classification (reversible / needs a hand-written rollback /
irreversible) plus the reverse script itself. In the first real use the script was never going to
be executed: the team fixes forward (EF idempotent scripts, `CREATE OR ALTER`, seed data), so
every run showed `Rollback: incomplete`, a REV-002 finding per file and a script nobody reads.
The signal decays to noise when it is always the same.

## Options considered

- **Opt-in flag `--rollback` (+ config key)** — default output carries no rollback artefacts;
  teams that do roll back turn it on once in `.planizer.json`. Chosen.
- Keep on, hide only the text line — JSON/markdown and REV-002 findings would still carry it.
- Remove the feature — loses the mechanical answer for teams that do roll back (and §4 of RULES.md).

## Decision

`PlanizerConfig.Rollback` (default `false`), set by `--rollback` or `"rollback": true`. When off:
no reverse script is built, MSSQL-REV-002 yields nothing, `ScriptSummary.RollbackComplete` is
`null`, and the text/markdown writers omit the rollback line/section. MSSQL-REV-001 (irreversible
data loss) is independent and always runs; `IrreversibleCount` is unaffected.

## Consequences

- Default reports are shorter and exit codes no longer depend on REV-002.
- Fixtures for REV-002 declare `-- planizer-test: rollback=true`; the harness supports the option.
- The GitHub Action exposes a `rollback` input (default `'false'`).
- `rollbackComplete` became nullable in the JSON schema (`null` = not analyzed).
