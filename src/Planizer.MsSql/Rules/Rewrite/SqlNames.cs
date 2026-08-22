using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>Shared name-rendering helpers for the rewrite rule family.</summary>
internal static class SqlNames
{
    /// <summary>Renders a multi-part name as <c>schema.table</c>; falls back when absent.</summary>
    public static string Render(SchemaObjectName? name, string fallback)
        => name is null || name.Identifiers.Count == 0
            ? fallback
            : string.Join(".", name.Identifiers.Select(i => i.Value));

    /// <summary>Renders a schema object name as "dbo.Orders"; falls back to "the table".</summary>
    public static string Table(SchemaObjectName? name)
        => Render(name, "the table");

    /// <summary>Renders a data type as written, e.g. "nvarchar(500)", "decimal(18, 4)" or "xml".</summary>
    public static string DataType(DataTypeReference? type)
    {
        if (type?.Name is null || type.Name.Identifiers.Count == 0)
        {
            return "the new type";
        }

        var name = string.Join(".", type.Name.Identifiers.Select(i => i.Value));
        if (type is ParameterizedDataTypeReference { Parameters.Count: > 0 } parameterized)
        {
            return $"{name}({string.Join(", ", parameterized.Parameters.Select(p => p.Value))})";
        }

        return name;
    }
}
