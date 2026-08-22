using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-016: row width vs the 8060-byte in-row limit.
/// <list type="bullet">
/// <item>CREATE TABLE — the declared fixed-width columns are fully known offline; when their
/// total exceeds 8060 bytes the table cannot hold a full row in-page → Warning.</item>
/// <item>ALTER TABLE ADD of a fixed-width column — the current row width is unknown offline, so
/// the rule reports Info + <c>Inconclusive</c> with the byte count it would add. This is the
/// reference implementation of the Inconclusive mechanism: no data, but the rule does not stay
/// silent.</item>
/// </list>
/// Variable-length types (var*, MAX, XML, …) are excluded from the arithmetic. The byte sizes
/// come from <see cref="SqlTypeWidths"/>, shared with the index key size rule (MSSQL-LIM-001).
/// </summary>
public sealed class RowWidthRule : MsSqlRuleBase
{
    private const int MaxInRowBytes = 8060;

    public override string Id => "MSSQL-RW-016";
    public override string Title => "Row width against the 8060-byte in-row limit";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            switch (statement.Ast)
            {
                case CreateTableStatement create when AnalyzeCreateTable(statement, create) is { } finding:
                    yield return finding;
                    break;

                case AlterTableAddTableElementStatement add:
                    foreach (var finding in AnalyzeAddColumn(context, statement, add))
                    {
                        yield return finding;
                    }

                    break;
            }
        }
    }

    private Finding? AnalyzeCreateTable(SqlStatementInfo statement, CreateTableStatement create)
    {
        var total = 0;
        foreach (var column in create.Definition?.ColumnDefinitions ?? [])
        {
            if (column.ComputedColumnExpression is null && SqlTypeWidths.FixedWidthBytes(column.DataType) is { } bytes)
            {
                total += bytes;
            }
        }

        if (total <= MaxInRowBytes)
        {
            return null;
        }

        var table = SqlNames.Render(create.SchemaObjectName, "the table");
        return CreateFinding(statement, DefaultSeverity,
            $"Declared fixed-width columns of {table} total {total} bytes, exceeding the " +
            $"{MaxInRowBytes}-byte in-row limit; INSERT/UPDATE fails whenever a row cannot fit.",
            fix: "Shrink or split the fixed-width columns, or switch rarely-filled wide char/binary " +
                 "columns to variable-length types.");
    }

    private IEnumerable<Finding> AnalyzeAddColumn(
        MsSqlAnalysisContext context, SqlStatementInfo statement, AlterTableAddTableElementStatement add)
    {
        if (context.Schema.IsAvailable)
        {
            yield break; // with a schema provider the real row width check runs instead (Phase 2)
        }

        var table = SqlNames.Render(add.SchemaObjectName, "the table");

        foreach (var column in add.Definition?.ColumnDefinitions ?? [])
        {
            if (column.ComputedColumnExpression is not null
                || SqlTypeWidths.FixedWidthBytes(column.DataType) is not { } bytes)
            {
                continue;
            }

            var name = column.ColumnIdentifier?.Value ?? "the new column";
            var unit = bytes == 1 ? "byte" : "bytes";
            yield return CreateFinding(statement, Severity.Info,
                $"Cannot verify the current row width of {table} offline; adding {name} " +
                $"({SqlTypeWidths.Describe(column.DataType)}) grows each row by {bytes} {unit} toward the " +
                $"{MaxInRowBytes}-byte in-row limit.",
                inconclusive: true);
        }
    }
}
