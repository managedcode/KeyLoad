using System.Collections.Frozen;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlProjectionAliases
{
    private static readonly FrozenSet<string> StructuralKeywords = new[]
    {
        SqlSyntax.Explain, SqlSyntax.Select, SqlSyntax.All, SqlSyntax.From, SqlSyntax.Inner,
        SqlSyntax.Join, SqlSyntax.On, SqlSyntax.Events, SqlSyntax.TopicEvents, SqlSyntax.QueueMessages,
        SqlSyntax.As, SqlSyntax.Where, SqlSyntax.Order, SqlSyntax.By, SqlSyntax.Desc, SqlSyntax.Asc,
        SqlSyntax.Using,
        SqlSyntax.Limit, SqlSyntax.Offset, SqlSyntax.Row, SqlSyntax.Rows, SqlSyntax.Fetch,
        SqlSyntax.First, SqlSyntax.Next, SqlSyntax.Only, SqlSyntax.With, SqlSyntax.Nulls,
        SqlSyntax.Last, SqlSyntax.Not, SqlSyntax.Is, SqlSyntax.IsNull, SqlSyntax.NotNull, SqlSyntax.Distinct, SqlSyntax.Missing,
        SqlSyntax.Null, SqlSyntax.In, SqlSyntax.Between, SqlSyntax.Symmetric, SqlSyntax.Asymmetric, SqlSyntax.And,
        SqlSyntax.Or, SqlSyntax.True, SqlSyntax.False, SqlSyntax.Unknown
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static string Read(SqlTokenCursor cursor, string fallback)
    {
        if (cursor.Eat(SqlSyntax.As))
        { return cursor.Identifier(); }
        if (cursor.Current.Kind != SqlTokenKind.Identifier)
        { return fallback; }
        if (!cursor.Current.Quoted && StructuralKeywords.Contains(cursor.Current.Text))
        { return fallback; }
        return cursor.Identifier();
    }
}
