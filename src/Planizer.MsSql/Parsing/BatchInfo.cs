namespace Planizer.MsSql;

/// <summary>
/// One <c>GO</c>-separated batch. <paramref name="Index"/> is global across the analyzed files
/// (like <see cref="SqlStatementInfo.Index"/>); <paramref name="StatementIndices"/> lists every
/// statement in the batch in pre-order, nested ones included. An empty batch (only comments or
/// a stray <c>GO</c>) is not recorded.
/// </summary>
public sealed record BatchInfo(int Index, int FirstStatementIndex, IReadOnlyList<int> StatementIndices);
