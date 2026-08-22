using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Parsing;

/// <summary>
/// Storage-size arithmetic for SQL Server data types, shared by every rule that reasons about a
/// byte limit (MSSQL-RW-016 row width, MSSQL-LIM-001 index key size). Sizes are the documented
/// on-disk storage sizes; declared lengths of variable types are their maximum.
/// </summary>
public static class SqlTypeWidths
{
    /// <summary>
    /// Fixed storage size in bytes for parameterless fixed-width types. Parameterized types
    /// (char/nchar/binary/decimal/float/datetime2/time/datetimeoffset) are computed in
    /// <see cref="FixedWidthBytes"/>; variable-length types are absent on purpose.
    /// </summary>
    private static readonly Dictionary<SqlDataTypeOption, int> FixedBytes = new()
    {
        [SqlDataTypeOption.BigInt] = 8,
        [SqlDataTypeOption.Int] = 4,
        [SqlDataTypeOption.SmallInt] = 2,
        [SqlDataTypeOption.TinyInt] = 1,
        [SqlDataTypeOption.Bit] = 1, // 1/8 byte, rounded up conservatively
        [SqlDataTypeOption.Money] = 8,
        [SqlDataTypeOption.SmallMoney] = 4,
        [SqlDataTypeOption.Real] = 4,
        [SqlDataTypeOption.DateTime] = 8,
        [SqlDataTypeOption.SmallDateTime] = 4,
        [SqlDataTypeOption.Date] = 3,
        [SqlDataTypeOption.UniqueIdentifier] = 16,
        [SqlDataTypeOption.Timestamp] = 8,
        [SqlDataTypeOption.Rowversion] = 8,
    };

    /// <summary>Storage bytes of a fixed-width type; <c>null</c> for variable-length or unknown types.</summary>
    public static int? FixedWidthBytes(DataTypeReference? dataType)
    {
        if (dataType is not SqlDataTypeReference sql)
        {
            return null; // XML, CLR/user-defined, … — not fixed-width as far as we can tell
        }

        if (FixedBytes.TryGetValue(sql.SqlDataTypeOption, out var fixedSize))
        {
            return fixedSize;
        }

        var parameter = FirstIntParameter(sql);
        return sql.SqlDataTypeOption switch
        {
            SqlDataTypeOption.Char or SqlDataTypeOption.Binary => parameter ?? 1,
            SqlDataTypeOption.NChar => 2 * (parameter ?? 1),
            SqlDataTypeOption.Decimal or SqlDataTypeOption.Numeric => (parameter ?? 18) switch
            {
                <= 9 => 5,
                <= 19 => 9,
                <= 28 => 13,
                _ => 17,
            },
            SqlDataTypeOption.Float => (parameter ?? 53) <= 24 ? 4 : 8,
            SqlDataTypeOption.DateTime2 => (parameter ?? 7) switch { <= 2 => 6, <= 4 => 7, _ => 8 },
            SqlDataTypeOption.Time => (parameter ?? 7) switch { <= 2 => 3, <= 4 => 4, _ => 5 },
            SqlDataTypeOption.DateTimeOffset => (parameter ?? 7) switch { <= 2 => 8, <= 4 => 9, _ => 10 },
            _ => null, // varchar/nvarchar/varbinary, MAX types, text/ntext/image, sql_variant, …
        };
    }

    /// <summary>
    /// Largest number of bytes a value of the type occupies in an index key: the fixed size for
    /// fixed-width types, the declared maximum for <c>varchar(n)</c>/<c>varbinary(n)</c> (n) and
    /// <c>nvarchar(n)</c> (2n). <c>null</c> for LOB/MAX types (never key-eligible), <c>sql_variant</c>,
    /// CLR and alias types, whose width the script alone cannot tell.
    /// </summary>
    public static int? MaxKeyBytes(DataTypeReference? dataType)
    {
        if (FixedWidthBytes(dataType) is { } fixedSize)
        {
            return fixedSize;
        }

        if (dataType is not SqlDataTypeReference sql || IsLargeObject(sql))
        {
            return null;
        }

        var parameter = FirstIntParameter(sql);
        return sql.SqlDataTypeOption switch
        {
            SqlDataTypeOption.VarChar or SqlDataTypeOption.VarBinary => parameter ?? 1,
            SqlDataTypeOption.NVarChar => 2 * (parameter ?? 1),
            _ => null,
        };
    }

    /// <summary>
    /// Whether the type is a large-object type that SQL Server refuses as an index key column:
    /// <c>varchar(max)</c>, <c>nvarchar(max)</c>, <c>varbinary(max)</c>, <c>text</c>, <c>ntext</c>,
    /// <c>image</c>, <c>xml</c>, <c>json</c>, <c>vector</c>.
    /// </summary>
    public static bool IsLargeObject(DataTypeReference? dataType) => dataType switch
    {
        XmlDataTypeReference => true,
        SqlDataTypeReference sql => sql.SqlDataTypeOption switch
        {
            SqlDataTypeOption.Text or SqlDataTypeOption.NText or SqlDataTypeOption.Image
                or SqlDataTypeOption.Json or SqlDataTypeOption.Vector => true,
            SqlDataTypeOption.VarChar or SqlDataTypeOption.NVarChar or SqlDataTypeOption.VarBinary
                => sql.Parameters.Count > 0 && sql.Parameters[0].LiteralType == LiteralType.Max,
            _ => false,
        },
        _ => false,
    };

    /// <summary>Renders a type for messages, e.g. "char(50)", "decimal(19, 4)", "xml"; "unknown type" when absent.</summary>
    public static string Describe(DataTypeReference? dataType)
    {
        if (dataType is SqlDataTypeReference sql)
        {
            var name = sql.SqlDataTypeOption.ToString().ToLowerInvariant();
            return sql.Parameters.Count == 0
                ? name
                : $"{name}({string.Join(", ", sql.Parameters.Select(p => p.Value))})";
        }

        if (dataType?.Name is { Identifiers.Count: > 0 } typeName)
        {
            return string.Join(".", typeName.Identifiers.Select(i => i.Value)).ToLowerInvariant();
        }

        return "unknown type";
    }

    private static int? FirstIntParameter(SqlDataTypeReference sql)
        => sql.Parameters.Count > 0
            && int.TryParse(sql.Parameters[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
