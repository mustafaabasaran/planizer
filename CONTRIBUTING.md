# Contributing to Planizer

Thanks for helping! The most valuable contributions right now, in order:

1. **False-positive reports** — a rule fired on SQL it should not have flagged. Use the
   *False positive* issue form; the minimal SQL you paste becomes a regression fixture.
2. **Rule proposals** — a dangerous T-SQL migration behavior Planizer does not catch yet.
3. **Code** — fixing an open issue or implementing an agreed rule proposal. For anything
   larger than a small fix, please open an issue first so the approach is agreed before you
   invest time.

## Ground rules (project principles)

- **No LLM, deterministic only.** Every rule is parser + rule table + fixed logic. Findings
  must be reproducible byte-for-byte.
- **Rules never stay silent.** When offline analysis cannot decide, report the finding marked
  `inconclusive` instead of dropping it.
- **Don't guess SQL Server behavior — measure it.** Any claim about locking, data movement or
  reversibility lives in `src/Planizer.MsSql/Catalog/mssql-ddl-behavior.csv` and is verified
  against a real SQL Server (Developer and Express) by the catalog-verification workflow in CI.
  If your change asserts a new behavior, add or adjust a catalog row and, ideally, a probe in
  `tests/Planizer.CatalogVerification.Tests`.
- **Worst-case defaults.** Without flags, analysis assumes SQL Server 2019 Standard.

## Getting started

You need the .NET 10 SDK. Then:

```sh
dotnet build          # zero warnings expected (warnings are errors)
dotnet test           # runs everything; the catalog-verification tests always skip locally
```

The catalog-verification tests run **only in CI** (they need a real SQL Server container and are
gated behind `PLANIZER_CATALOG_VERIFY=1`). Never set that variable on a development machine.

## Anatomy of a rule

Every rule ships as a set; a PR adding a rule should contain all of these:

1. **Catalog first** (if the rule makes a behavior claim): a row in
   `src/Planizer.MsSql/Catalog/mssql-ddl-behavior.csv` or
   `mssql-feature-versions.csv`.
2. **Fixtures**: at least one triggering and one non-triggering `.sql` file under
   `tests/Planizer.Tests/Fixtures/<RULE-ID>/`. Files named `trigger*.sql` need at least one
   `expect`, `clean*.sql` at least one `expect-none`. Expectations are comment directives:

   ```sql
   -- planizer-test: version=2019 edition=Standard   (optional; defaults otherwise)
   -- expect: MSSQL-RW-002 severity=Critical line=4  (severity/line optional)
   -- expect-none: MSSQL-RW-001
   ```

   The fixture harness discovers these automatically — no C# needed for the happy path.
3. **The rule class**, under the matching `src/Planizer.MsSql/Rules/<Category>/` folder,
   inheriting `MsSqlRuleBase`.
4. **A registry line** in `src/Planizer.MsSql/RuleRegistry.cs`. Rule discovery is an explicit
   list (Native AOT trimming removes types reflection would find); forgetting the line fails
   `RuleRegistryTests`.
5. **A documentation page**: `docs/rules/<RULE-ID>.md` — what fires, why it matters, how to fix,
   with sources for any behavior claim.

Every finding follows the same shape: rule id + severity + location + one-sentence rationale +
suggested fix where possible + the version/edition assumption it was produced under.

## Pull requests

- Keep PRs focused — one rule or one fix per PR.
- `dotnet build` with zero warnings and `dotnet test` green are required; CI also runs the
  composite action against `samples/`.
- If a change intentionally alters existing findings (counts, severities, messages), say so in
  the PR description and update the affected fixtures in the same PR.
