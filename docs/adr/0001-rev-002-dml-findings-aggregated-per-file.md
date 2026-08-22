# ADR-0001: Aggregate REV-002 DML findings per file at Info severity

- Status: accepted
- Date: 2026-08-21
- Decision source: option-pick

## Context

MSSQL-REV-002 flags every state-changing statement for which the rollback builder cannot derive
an inverse. For DDL that is rare and valuable (7 findings across a private corpus of 24 production repositories). For DML
it is the norm: the inverse of an `INSERT`/`UPDATE`/`DELETE` depends on previous row values that
are unknowable offline. Scanning 8,507 real migrations (547k statements) produced 136,541
per-statement REV-002 warnings — 99.8 % of all findings — almost entirely seed-data and cleanup
scripts (`OuterDML`, `DML`). At that volume the rule hides every other finding and fails
`--fail-on warning` on every seed script.

## Options considered

- **One Info finding per file for DML** — keeps the signal ("this file needs a hand-written
  rollback") and the `rollbackComplete=false` summary, removes the per-statement flood; DDL keeps
  per-statement Warning. Chosen.
- Per-statement but Info — no longer breaks CI, but still 136k lines of output.
- Silence REV-002 for DML entirely — only the summary line would say the rollback is incomplete;
  loses the count/verb breakdown reviewers use to judge effort.
- Keep as is, rely on `.planizer.json` overrides — pushes the noise problem to every adopter.

## Decision

REV-002 emits, per analyzed file, a single Info finding anchored at the first offending DML
statement: "N data-modification statements in this file have no automatic inverse (INSERT×a,
UPDATE×b, DELETE×c); the rollback script is incomplete". Statements carrying a
`planizer:ignore MSSQL-REV-002` suppression are excluded from the count. DDL without an inverse is
still reported per statement at Warning.

## Consequences

- Per-file aggregate findings become an accepted shape for rules whose per-statement form is
  inherently noisy; the anchor is the first contributing statement.
- The rollback summary (`rollbackComplete`, `rollbackScript`) is unchanged and remains the
  machine-readable signal.
- A DML file with any un-suppressed write always shows one Info line; teams wanting silence
  disable the rule in `.planizer.json`.
- Fixtures and docs for REV-002 describe both shapes (DDL per statement, DML per file).
