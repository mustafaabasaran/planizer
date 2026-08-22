using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-014: ALTER TABLE … ADD CONSTRAINT CHECK / FOREIGN KEY validates every existing row
/// under a Sch-M lock (WITH CHECK is the default for newly added constraints). The WITH NOCHECK
/// variant skips the scan but leaves the constraint untrusted — the optimizer will not use it.
/// Both variants are reported: the scanning one as Warning, the NOCHECK trade-off as Info.
/// </summary>
public sealed class CheckOrForeignKeyConstraintRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-014";
    public override string Title => "Adding a CHECK/FOREIGN KEY constraint scans all existing rows";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (statement.Ast is not AlterTableAddTableElementStatement add)
            {
                continue;
            }

            var table = SqlNames.Render(add.SchemaObjectName, "the table");

            foreach (var constraint in add.Definition?.TableConstraints ?? [])
            {
                var kind = constraint switch
                {
                    CheckConstraintDefinition => "CHECK",
                    ForeignKeyConstraintDefinition => "FOREIGN KEY",
                    _ => null,
                };

                if (kind is null)
                {
                    continue;
                }

                var name = constraint.ConstraintIdentifier?.Value ?? $"the {kind} constraint";

                if (add.ExistingRowsCheckEnforcement == ConstraintEnforcement.NoCheck)
                {
                    yield return CreateFinding(statement, Severity.Info,
                        $"WITH NOCHECK skips validating existing rows of {table}, but {kind} " +
                        $"constraint {name} stays untrusted: the optimizer will not use it for plans.",
                        fix: $"After a low-traffic validation window, run: ALTER TABLE {table} " +
                             $"WITH CHECK CHECK CONSTRAINT {name};");
                }
                else
                {
                    var behavior = context.Catalog.Lookup(
                        DdlOperationKeys.AddCheckOrFk, context.Config.TargetVersion, context.Config.Edition);

                    if (behavior is null)
                    {
                        yield return CreateFinding(statement, Severity.Warning,
                            $"Adding {kind} constraint {name} on {table}: behavior is not cataloged " +
                            "for this target — review manually.",
                            inconclusive: true);
                    }
                    else
                    {
                        yield return CreateFinding(statement, DefaultSeverity,
                            $"Adding {kind} constraint {name} scans every existing row of {table} " +
                            "under a Sch-M lock (WITH CHECK is the default).",
                            fix: $"Add it as WITH NOCHECK to skip the scan, then validate later with " +
                                 $"ALTER TABLE {table} WITH CHECK CHECK CONSTRAINT {name}; — until " +
                                 "then the constraint is untrusted.");
                    }
                }
            }
        }
    }
}
