using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql;

/// <summary>A ScriptDom parse error mapped to Planizer's location model.</summary>
public sealed record MsSqlParseError(SourceLocation Location, string Message);

/// <summary>
/// Result of parsing one file: the deploy-time statements (across GO batches, nested control-flow
/// bodies included), the batches they belong to, and parse errors.
/// </summary>
public sealed class MsSqlParseResult
{
    public required IReadOnlyList<SqlStatementInfo> Statements { get; init; }
    public required IReadOnlyList<BatchInfo> Batches { get; init; }
    public required IReadOnlyList<MsSqlParseError> Errors { get; init; }
}

/// <summary>
/// ScriptDom wrapper: picks the <see cref="TSqlParser"/> for the target version (or an explicit
/// <see cref="SqlGrammar"/>), flattens <c>TSqlScript.Batches</c> into a statement list via
/// <see cref="StatementFlattener"/>, classifies each statement, and binds <c>planizer:ignore</c>
/// suppressions.
/// </summary>
public sealed class MsSqlScriptParser
{
    /// <summary>
    /// Parses one file with the grammar of the target version. <paramref name="indexOffset"/> is
    /// added to every statement's <see cref="SqlStatementInfo.Index"/> and
    /// <paramref name="batchIndexOffset"/> to every <see cref="SqlStatementInfo.BatchIndex"/>, so
    /// both stay global when analyzing multiple files.
    /// </summary>
    public MsSqlParseResult Parse(
        string sql,
        string filePath,
        SqlServerVersion targetVersion,
        int indexOffset = 0,
        int batchIndexOffset = 0)
        => Parse(sql, filePath, SqlGrammar.For(targetVersion), indexOffset, batchIndexOffset);

    /// <summary>
    /// Parses one file with an explicit grammar level. The analyzer uses this to re-parse a file
    /// the target grammar rejected with newer grammars (MSSQL-VER-001 vs MSSQL-PARSE-001).
    /// </summary>
    public MsSqlParseResult Parse(
        string sql,
        string filePath,
        SqlGrammar grammar,
        int indexOffset = 0,
        int batchIndexOffset = 0)
    {
        var parser = grammar.CreateParser();

        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out IList<ParseError>? parseErrors);

        var errors = (parseErrors ?? [])
            .Select(e => new MsSqlParseError(new SourceLocation(filePath, e.Line, e.Column), e.Message))
            .ToList();

        var statements = new List<SqlStatementInfo>();
        var batches = new List<BatchInfo>();

        if (fragment is TSqlScript script)
        {
            var directives = SuppressionScanner.Scan(script.ScriptTokenStream ?? []);
            var index = indexOffset;
            var batchIndex = batchIndexOffset;

            foreach (var batch in script.Batches)
            {
                var firstInBatch = statements.Count;
                var currentBatch = batchIndex;

                StatementFlattener.Flatten(
                    batch.Statements,
                    (statement, flow) =>
                    {
                        var (ownRuleIds, ownReason) = SuppressionScanner.Resolve(directives, statement.StartLine);
                        var (suppressedRuleIds, suppressReason) = Inherit(ownRuleIds, ownReason, flow.Parent);

                        return new SqlStatementInfo
                        {
                            Ast = statement,
                            Kind = StatementClassifier.Classify(statement),
                            Location = new SourceLocation(filePath, statement.StartLine, statement.StartColumn),
                            Sql = GetSql(statement),
                            Index = index++,
                            SuppressedRuleIds = suppressedRuleIds,
                            SuppressReason = suppressReason,
                            BatchIndex = currentBatch,
                            Depth = flow.Depth,
                            Parent = flow.Parent,
                            EnclosingIf = flow.EnclosingIf,
                            InElseBranch = flow.InElseBranch,
                            InTryBlock = flow.InTryBlock,
                            InCatchBlock = flow.InCatchBlock,
                            InWhileLoop = flow.InWhileLoop,
                        };
                    },
                    statements);

                if (statements.Count == firstInBatch)
                {
                    continue; // empty batch (comments only / stray GO): no index consumed
                }

                batches.Add(new BatchInfo(
                    currentBatch,
                    statements[firstInBatch].Index,
                    statements.Skip(firstInBatch).Select(s => s.Index).ToList()));
                batchIndex++;
            }
        }

        return new MsSqlParseResult { Statements = statements, Batches = batches, Errors = errors };
    }

    /// <summary>
    /// A suppression on an enclosing block applies to everything inside it: the statement's own
    /// directive plus its parent's (already transitive) set. The statement's own reason wins.
    /// </summary>
    private static (IReadOnlySet<string> RuleIds, string? Reason) Inherit(
        IReadOnlySet<string> ownRuleIds,
        string? ownReason,
        SqlStatementInfo? parent)
    {
        if (parent is null || parent.SuppressedRuleIds.Count == 0)
        {
            return (ownRuleIds, ownReason);
        }

        var merged = new HashSet<string>(ownRuleIds, StringComparer.OrdinalIgnoreCase);
        merged.UnionWith(parent.SuppressedRuleIds);
        return (merged, ownReason ?? parent.SuppressReason);
    }

    private static string GetSql(TSqlFragment fragment)
    {
        if (fragment.ScriptTokenStream is null || fragment.FirstTokenIndex < 0 || fragment.LastTokenIndex < 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex; i++)
        {
            builder.Append(fragment.ScriptTokenStream[i].Text);
        }

        return builder.ToString();
    }
}
