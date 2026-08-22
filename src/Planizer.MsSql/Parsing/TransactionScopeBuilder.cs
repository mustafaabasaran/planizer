using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql;

/// <summary>
/// A <c>BEGIN TRAN … COMMIT/ROLLBACK</c> range over statement indices.
/// For a closed scope <paramref name="EndIndex"/> is the COMMIT/ROLLBACK statement (excluded from
/// <paramref name="StatementIndices"/>); for a scope left open it is the script's last statement
/// (included). <paramref name="StatementIndices"/> holds every enclosed statement.
/// </summary>
public sealed record TransactionScope(int BeginIndex, int EndIndex, IReadOnlyList<int> StatementIndices);

/// <summary>Builds explicit-transaction scopes from one file's top-level statements.</summary>
public static class TransactionScopeBuilder
{
    /// <summary>
    /// Uses <see cref="SqlStatementInfo.Index"/> values, so pre-offset (global) indices flow through
    /// unchanged. Nested <c>BEGIN TRAN</c>s stay inside the outer scope; a scope without a COMMIT
    /// extends to the end of the script. Call per file — transactions never span files.
    /// </summary>
    public static IReadOnlyList<TransactionScope> Build(IReadOnlyList<SqlStatementInfo> statements)
    {
        var scopes = new List<TransactionScope>();
        int? beginIndex = null;
        var depth = 0;
        var enclosed = new List<int>();

        foreach (var statement in statements)
        {
            switch (statement.Ast)
            {
                case BeginTransactionStatement:
                    if (depth == 0)
                    {
                        beginIndex = statement.Index;
                        enclosed = [];
                    }
                    else
                    {
                        enclosed.Add(statement.Index);
                    }

                    depth++;
                    break;

                case CommitTransactionStatement or RollbackTransactionStatement:
                    if (depth == 0)
                    {
                        break; // stray COMMIT/ROLLBACK without a BEGIN — nothing to close
                    }

                    depth--;
                    if (depth == 0)
                    {
                        scopes.Add(new TransactionScope(beginIndex!.Value, statement.Index, enclosed));
                        beginIndex = null;
                    }
                    else
                    {
                        enclosed.Add(statement.Index);
                    }

                    break;

                default:
                    if (depth > 0)
                    {
                        enclosed.Add(statement.Index);
                    }

                    break;
            }
        }

        if (depth > 0 && beginIndex is { } openBegin)
        {
            scopes.Add(new TransactionScope(openBegin, statements[^1].Index, enclosed));
        }

        return scopes;
    }
}
