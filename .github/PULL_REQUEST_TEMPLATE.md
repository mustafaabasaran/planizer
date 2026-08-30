## What

<!-- One or two sentences: what does this PR change, and why? Link the issue if there is one. -->

## Checklist

- [ ] `dotnet build` has zero warnings and `dotnet test` is green
- [ ] Changes that alter existing findings (counts, severities, messages) are called out above
      and the affected fixtures are updated in this PR

**For a new or changed rule, also:**

- [ ] Triggering (`trigger*.sql`) and non-triggering (`clean*.sql`) fixtures under
      `tests/Planizer.Tests/Fixtures/<RULE-ID>/`
- [ ] The rule is listed in `src/Planizer.MsSql/RuleRegistry.cs`
- [ ] Documentation page `docs/rules/<RULE-ID>.md` (what fires, why, how to fix, sources)
- [ ] Behavior claims (locking, data movement, reversibility) are backed by a catalog row in
      `src/Planizer.MsSql/Catalog/` — ideally with a verification probe
