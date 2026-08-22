using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Parsing;

/// <summary>
/// Table-identity helpers for rules that ask "is this the same table?" across statements that
/// spell the name differently: <c>[T]</c>, <c>dbo.T</c>, <c>[DBO].[t]</c> and the table part of an
/// <c>sp_rename</c> string literal all denote one table.
/// </summary>
public static class TableNames
{
    private const string DefaultSchema = "dbo";

    /// <summary>
    /// Canonical comparison key: case-insensitive, quoting removed, and an unqualified name is
    /// assumed to live in <c>dbo</c> — migrations run as dbo, so <c>T</c> and <c>dbo.T</c> are the
    /// same table while <c>audit.T</c> is not.
    /// </summary>
    public static string Key(IReadOnlyList<string> parts)
    {
        IReadOnlyList<string> qualified = parts.Count == 1 ? [DefaultSchema, parts[0]] : parts;
        return string.Join(".", qualified.Select(p => p.ToLowerInvariant()));
    }

    /// <summary>Key of a parsed name; <c>null</c> when the AST carries no name.</summary>
    public static string? Key(SchemaObjectName? name)
        => name is null || name.Identifiers.Count == 0 ? null : Key(Parts(name));

    /// <summary>Identifier values (already unquoted by the parser) of a parsed name.</summary>
    public static IReadOnlyList<string> Parts(SchemaObjectName name)
        => name.Identifiers.Select(i => i.Value).ToList();

    /// <summary>Dotted display form, e.g. <c>dbo.Orders</c>.</summary>
    public static string Display(IReadOnlyList<string> parts) => string.Join(".", parts);

    /// <summary>
    /// Splits a name written inside a string literal the way <c>sp_rename</c> reads it —
    /// <c>[dbo].[T].[C]</c>, <c>dbo.T.C</c>, <c>"dbo"."T"</c> — honouring brackets and double
    /// quotes (a dot inside them does not split) and returning the parts unquoted.
    /// </summary>
    public static IReadOnlyList<string> SplitLiteral(string literal)
    {
        var parts = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < literal.Length; i++)
        {
            var c = literal[i];
            switch (c)
            {
                case '[':
                    i = ReadQuoted(literal, i + 1, ']', current);
                    break;
                case '"':
                    i = ReadQuoted(literal, i + 1, '"', current);
                    break;
                case '.':
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        parts.Add(current.ToString().Trim());
        return parts;
    }

    /// <summary>
    /// Copies a quoted identifier body into <paramref name="into"/> (a doubled closing quote is
    /// an escaped literal quote) and returns the index of the closing quote.
    /// </summary>
    private static int ReadQuoted(string text, int start, char close, StringBuilder into)
    {
        var i = start;
        for (; i < text.Length; i++)
        {
            if (text[i] != close)
            {
                into.Append(text[i]);
            }
            else if (i + 1 < text.Length && text[i + 1] == close)
            {
                into.Append(close);
                i++;
            }
            else
            {
                return i;
            }
        }

        return i; // unterminated: take the rest as the identifier
    }
}
