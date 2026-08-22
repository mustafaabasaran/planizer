using Planizer.Core;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// MSSQL-VER-001: a feature the target SQL Server version does not have. Two detection paths
/// share the rule id:
/// <list type="bullet">
/// <item><b>Catalog (this class):</b> <see cref="FeatureVersionCatalog"/> maps function names, AST
/// node types and index options to their minimum version. Target below the minimum → Blocker
/// ("not a recognized built-in function name" / "incorrect syntax" on the server). A bare 2016
/// target using a 2016 SP1 feature (CREATE OR ALTER) → Warning, because the patch level is
/// unknown offline.</item>
/// <item><b>Grammar (the analyzer):</b> syntax the target grammar rejects but a newer grammar
/// accepts is reported as VER-001 instead of MSSQL-PARSE-001, and the script is still analysed
/// with the newer parse. A catalog finding on the same statement supersedes the grammar one.</item>
/// </list>
/// Module bodies are scanned too: a procedure that references a function the server does not
/// know fails at CREATE time, not at first execution.
/// </summary>
public sealed class UnsupportedFeatureRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-VER-001";
    public override string Title => "Feature not available on the target SQL Server version";
    public override Severity DefaultSeverity => Severity.Blocker;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        var target = context.Config.TargetVersion;
        var targetLabel = TargetParser.VersionToken(target);

        foreach (var statement in context.Statements)
        {
            foreach (var fragment in StatementScan.OwnFragments(statement))
            {
                foreach (var use in context.Features.FindViolations(fragment, target))
                {
                    var feature = use.Feature;
                    var label = feature.Note is null ? feature.Label : $"{feature.Label} ({feature.Note})";

                    if (target < feature.MinVersion)
                    {
                        yield return CreateFinding(statement, DefaultSeverity,
                            $"{label} requires SQL Server {feature.MinVersionLabel}; the target is SQL Server {targetLabel}, " +
                            "where the statement fails.",
                            $"Raise --target-version to {feature.MinVersionLabel} if production really runs it; " +
                            $"otherwise replace {feature.Label} with an equivalent SQL Server {targetLabel} supports.");
                    }
                    else
                    {
                        // Same major version, service-pack gated: cannot be proven offline.
                        yield return CreateFinding(statement, Severity.Warning,
                            $"{label} requires SQL Server {feature.MinVersionLabel}; the target is a bare SQL Server " +
                            $"{targetLabel} whose patch level is unknown offline.",
                            $"Confirm the server is at least {feature.MinVersionLabel}, or state a newer " +
                            "--target-version so the check can be conclusive.",
                            inconclusive: true);
                    }
                }
            }
        }
    }
}
