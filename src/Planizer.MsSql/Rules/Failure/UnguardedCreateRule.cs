using Planizer.Core;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// MSSQL-IDEM-001: a CREATE TABLE / INDEX / VIEW / PROCEDURE / FUNCTION / TRIGGER / TYPE / SCHEMA /
/// SEQUENCE that is not wrapped in an existence check, is not <c>CREATE OR ALTER</c>, and whose
/// object the file did not safely drop earlier — an exit guard earlier in the batch
/// (<c>IF OBJECT_ID(…) IS NOT NULL RETURN;</c>) counts as a check. Fine on the first run; the second run (a retried
/// pipeline, a re-applied migration, a restored environment) fails with "There is already an
/// object named …" (2714) or "index … already exists" (1913).
/// </summary>
public sealed class UnguardedCreateRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-IDEM-001";
    public override string Title => "CREATE without an existence check is not re-runnable";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (IdempotencyTargets.Created(statement.Ast) is not ({ } created, false)
                || IdempotencyGuard.IsGuarded(statement, context)
                || DroppedSafelyEarlier(statement, context, created))
            {
                continue;
            }

            yield return CreateFinding(statement, DefaultSeverity,
                $"{created.Keyword} {created.Display} is not guarded by an existence check; " +
                $"running the script a second time fails because the {created.Kind} already exists{ErrorHint(created)}.",
                Fix(created, context.Config.TargetVersion));
        }
    }

    private static bool DroppedSafelyEarlier(SqlStatementInfo statement, MsSqlAnalysisContext context, SchemaObject created)
    {
        if (IdempotencyGuard.IsDroppedEarlierInFile(statement, context, created.Name))
        {
            return true;
        }

        // DROP TYPE / DROP SCHEMA are separate AST shapes the shared guard does not inspect.
        return IdempotencyTargets.EarlierInFile(statement, context).Any(earlier =>
            IdempotencyTargets.Dropped(earlier.Ast).Any(d =>
                d.Object.SameAs(created) && (d.IsIfExists || IdempotencyGuard.IsGuarded(earlier, context))));
    }

    private static string ErrorHint(SchemaObject created) => created.Kind switch
    {
        "index" or "columnstore index" => " (error 1913)",
        "type" => "",
        _ => " (error 2714)",
    };

    private static string Fix(SchemaObject created, SqlServerVersion target)
    {
        var objectWord = created.Keyword["CREATE ".Length..];

        if (created.IsModule)
        {
            if (IdempotencyTargets.SupportsCreateOrAlter(target))
            {
                return $"Use CREATE OR ALTER {objectWord} {created.Display} so the script can be re-run.";
            }

            if (target == SqlServerVersion.Sql2016)
            {
                return $"Use CREATE OR ALTER {objectWord} {created.Display} (requires SQL Server 2016 SP1); " +
                       $"on RTM drop it first in its own batch: IF {created.ExistsPredicate()} DROP {objectWord} {created.Display}; GO";
            }

            return $"Drop it first in its own batch: IF {created.ExistsPredicate()} DROP {objectWord} {created.Display}; GO";
        }

        if (created.Kind == "schema")
        {
            // CREATE SCHEMA must be the only statement in its batch, hence EXEC.
            return $"Guard it: IF {created.MissingPredicate()} EXEC(N'CREATE SCHEMA {created.Display}');";
        }

        return $"Guard it: IF {created.MissingPredicate()} BEGIN {created.Keyword} {created.Display} … END";
    }
}
