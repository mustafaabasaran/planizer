namespace Planizer.Core;

/// <summary>Per-rule configuration: disable a rule or override its severity.</summary>
public sealed record RuleOverride(bool Enabled = true, Severity? Severity = null);
