using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql;

/// <summary>
/// One parsed <c>-- planizer:ignore RULE_ID[, RULE_ID2] [free-text reason]</c> comment.
/// <paramref name="Standalone"/> is true when the comment is the first non-whitespace token on its
/// line; only standalone directives bind to the statement on the following line.
/// </summary>
public sealed record SuppressionDirective(
    int Line,
    IReadOnlyList<string> RuleIds,
    string? Reason,
    bool Standalone);

/// <summary>Collects <c>planizer:ignore</c> comments from the token stream and binds them to statements.</summary>
public static partial class SuppressionScanner
{
    [GeneratedRegex(@"^--\s*planizer:ignore\s+(?<rest>\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectivePattern();

    [GeneratedRegex(@"^(?<id>[A-Za-z0-9_.-]+)(?<more>(\s*,\s*[A-Za-z0-9_.-]+)*)\s*(?<reason>.*)$")]
    private static partial Regex IdListPattern();

    public static IReadOnlyList<SuppressionDirective> Scan(IEnumerable<TSqlParserToken> tokens)
    {
        var directives = new List<SuppressionDirective>();
        var currentLine = 0;
        var lineHasCode = false;

        foreach (var token in tokens)
        {
            if (token.Line != currentLine)
            {
                currentLine = token.Line;
                lineHasCode = false;
            }

            if (token.TokenType == TSqlTokenType.SingleLineComment)
            {
                if (Parse(token.Text, token.Line, standalone: !lineHasCode) is { } directive)
                {
                    directives.Add(directive);
                }
            }
            else if (token.TokenType is not TSqlTokenType.WhiteSpace and not TSqlTokenType.EndOfFile)
            {
                lineHasCode = true;
            }
        }

        return directives;
    }

    /// <summary>
    /// Resolves the directives that apply to a statement starting at <paramref name="statementStartLine"/>:
    /// a directive on the same line (trailing comment) or a standalone directive on the line directly above.
    /// </summary>
    public static (IReadOnlySet<string> RuleIds, string? Reason) Resolve(
        IReadOnlyList<SuppressionDirective> directives,
        int statementStartLine)
    {
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? reason = null;

        foreach (var directive in directives)
        {
            var applies = directive.Line == statementStartLine
                || (directive.Standalone && directive.Line == statementStartLine - 1);
            if (!applies)
            {
                continue;
            }

            ruleIds.UnionWith(directive.RuleIds);
            reason ??= directive.Reason;
        }

        return (ruleIds, reason);
    }

    private static SuppressionDirective? Parse(string commentText, int line, bool standalone)
    {
        var directiveMatch = DirectivePattern().Match(commentText.TrimEnd());
        if (!directiveMatch.Success)
        {
            return null;
        }

        var idListMatch = IdListPattern().Match(directiveMatch.Groups["rest"].Value);
        if (!idListMatch.Success)
        {
            return null;
        }

        var ruleIds = new List<string> { idListMatch.Groups["id"].Value };
        foreach (var part in idListMatch.Groups["more"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                ruleIds.Add(trimmed);
            }
        }

        var reason = idListMatch.Groups["reason"].Value.Trim();
        return new SuppressionDirective(line, ruleIds, reason.Length == 0 ? null : reason, standalone);
    }
}
