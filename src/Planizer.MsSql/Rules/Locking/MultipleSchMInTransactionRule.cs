using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-007: two or more Sch-M-acquiring statements inside one explicit transaction.
/// Every Sch-M lock is held until COMMIT, so the total blocking window is the sum of all the
/// statements. The finding is anchored to the FIRST Sch-M statement of the transaction.
/// </summary>
public sealed class MultipleSchMInTransactionRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-007";
    public override string Title => "Multiple Sch-M locks held until COMMIT in one transaction";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var scope in context.Transactions)
        {
            var schM = TransactionSchMScanner.SchMStatements(context, scope);
            if (schM.Count < 2)
            {
                continue;
            }

            yield return CreateFinding(schM[0], DefaultSeverity,
                $"This transaction takes schema-modification (Sch-M) locks in {schM.Count} statements and holds every one of them until COMMIT; the total blocking window is the sum of all of them.",
                "Split the transaction so each Sch-M statement commits on its own.");
        }
    }
}
