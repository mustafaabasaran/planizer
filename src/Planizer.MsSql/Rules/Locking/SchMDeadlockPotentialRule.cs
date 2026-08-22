using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-008: one explicit transaction takes Sch-M locks on two or more DIFFERENT tables.
/// Any concurrent session locking the same tables in another order can deadlock with the
/// migration. Anchored to the first Sch-M statement of the transaction.
/// </summary>
public sealed class SchMDeadlockPotentialRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-008";
    public override string Title => "Sch-M locks on multiple tables in one transaction (deadlock potential)";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var scope in context.Transactions)
        {
            var schM = TransactionSchMScanner.SchMStatements(context, scope);
            if (schM.Count == 0)
            {
                continue;
            }

            // [T], dbo.T and the table inside an sp_rename literal are one table: group by the
            // canonical key, print the first spelling seen.
            var tables = schM
                .SelectMany(TransactionSchMScanner.LockedTables)
                .GroupBy(t => t.Key, StringComparer.Ordinal)
                .Select(g => g.First().Display)
                .ToList();

            if (tables.Count < 2)
            {
                continue;
            }

            yield return CreateFinding(schM[0], DefaultSeverity,
                $"This transaction takes schema-modification (Sch-M) locks on {tables.Count} different tables ({string.Join(", ", tables)}); sessions locking them in another order can deadlock.",
                "Modify the tables in separate transactions, and always lock them in a consistent order.");
        }
    }
}
