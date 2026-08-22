using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// MSSQL-BATCH-002: a variable declared in one batch is used in a later batch of the same file
/// without being declared again. <c>GO</c> ends the scope of every variable, so the later batch
/// fails to compile with error 137 (Must declare the scalar variable) or 1087 (Must declare the
/// table variable). Declaration order inside a batch is not checked, parameter names of a
/// called procedure (<c>EXEC p @name = …</c>) are not variable uses, and module bodies are not
/// walked.
/// </summary>
public sealed class VariableAcrossBatchRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-BATCH-002";
    public override string Title => "Variable declared in an earlier batch is used after GO";
    public override Severity DefaultSeverity => Severity.Blocker;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        var declarations = CollectDeclarations(context.Statements);

        foreach (var statement in context.Statements)
        {
            if (StatementScan.IsModuleDefinition(statement.Ast)
                || !declarations.TryGetValue(statement.Location.File, out var inFile))
            {
                continue;
            }

            var outOfScope = new List<Declaration>();
            foreach (var name in UsedVariables(statement))
            {
                if (!inFile.TryGetValue(name, out var candidates)
                    || candidates.Any(d => d.BatchIndex == statement.BatchIndex))
                {
                    continue; // never declared (a different error), or declared in this batch
                }

                var earlier = candidates
                    .Where(d => d.BatchIndex < statement.BatchIndex)
                    .OrderByDescending(d => d.BatchIndex)
                    .ThenByDescending(d => d.Statement.Index)
                    .FirstOrDefault();

                if (earlier is not null && !outOfScope.Contains(earlier))
                {
                    outOfScope.Add(earlier);
                }
            }

            if (outOfScope.Count > 0)
            {
                yield return Report(statement, outOfScope);
            }
        }
    }

    private Finding Report(SqlStatementInfo statement, List<Declaration> outOfScope)
    {
        var allTable = outOfScope.All(d => d.IsTable);
        var allScalar = outOfScope.All(d => !d.IsTable);
        var error = allScalar ? "error 137 (Must declare the scalar variable"
            : allTable ? "error 1087 (Must declare the table variable"
            : "error 137 / 1087 (Must declare the variable";

        string message;
        if (outOfScope.Count == 1)
        {
            var single = outOfScope[0];
            message = $"{single.Name} is used here but was declared at line {single.Statement.Location.Line} in an " +
                      "earlier batch; GO ends the scope of every variable, so this batch fails to compile with " +
                      $"{error} \"{single.Name}\").";
        }
        else
        {
            var names = string.Join(", ", outOfScope.Select(d => $"{d.Name} (declared at line {d.Statement.Location.Line})"));
            message = $"Variables {names} are used here but belong to an earlier batch; GO ends the scope of " +
                      $"every variable, so this batch fails to compile with {error}).";
        }

        return CreateFinding(statement, Severity.Blocker, message,
            fix: "Re-declare in this batch, before the first use:\n" +
                 string.Join('\n', outOfScope.Select(d => d.RedeclareSql)));
    }

    /// <summary>file → variable name → declarations, in script order.</summary>
    private static Dictionary<string, Dictionary<string, List<Declaration>>> CollectDeclarations(
        IReadOnlyList<SqlStatementInfo> statements)
    {
        var result = new Dictionary<string, Dictionary<string, List<Declaration>>>(StringComparer.Ordinal);

        foreach (var statement in statements)
        {
            foreach (var declaration in Declarations(statement))
            {
                if (!result.TryGetValue(statement.Location.File, out var inFile))
                {
                    result[statement.Location.File] = inFile = new Dictionary<string, List<Declaration>>(StringComparer.OrdinalIgnoreCase);
                }

                if (!inFile.TryGetValue(declaration.Name, out var list))
                {
                    inFile[declaration.Name] = list = [];
                }

                list.Add(declaration);
            }
        }

        return result;
    }

    private static IEnumerable<Declaration> Declarations(SqlStatementInfo statement)
    {
        switch (statement.Ast)
        {
            case DeclareVariableStatement declare:
                foreach (var element in declare.Declarations)
                {
                    if (element.VariableName?.Value is { Length: > 0 } name)
                    {
                        yield return new Declaration(name, statement, statement.BatchIndex, IsTable: false,
                            $"DECLARE {StatementScan.Text(element)};");
                    }
                }

                break;

            case DeclareTableVariableStatement { Body.VariableName.Value: { Length: > 0 } name }:
                var sql = StatementScan.Text(statement.Ast).TrimEnd();
                yield return new Declaration(name, statement, statement.BatchIndex, IsTable: true,
                    sql.EndsWith(';') ? sql : sql + ";");
                break;
        }
    }

    /// <summary>Distinct variable names the statement itself uses, in source order of first appearance.</summary>
    private static IEnumerable<string> UsedVariables(SqlStatementInfo statement)
    {
        var collector = new VariableUseCollector();
        foreach (var fragment in StatementScan.OwnFragments(statement))
        {
            fragment.Accept(collector);
        }

        // Visit order follows the AST (e.g. WHERE before SET); report in source order instead.
        return collector.Uses
            .OrderBy(u => u.Offset)
            .Select(u => u.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record Declaration(string Name, SqlStatementInfo Statement, int BatchIndex, bool IsTable, string RedeclareSql);

    private sealed class VariableUseCollector : TSqlFragmentVisitor
    {
        public List<(string Name, int Offset)> Uses { get; } = [];

        public override void Visit(VariableReference node)
        {
            if (node.Name is { Length: > 0 } name)
            {
                Uses.Add((name, node.StartOffset));
            }
        }

        /// <summary><c>EXEC p @param = value</c>: @param names the callee's parameter, only the value is a use.</summary>
        public override void ExplicitVisit(ExecuteParameter node) => node.ParameterValue?.Accept(this);
    }
}
