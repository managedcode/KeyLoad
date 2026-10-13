using System.Globalization;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlOrderOrdinal
{
    private const int FirstOrdinal = 1;
    internal static string Read(SqlTokenCursor cursor, List<Selection> projection, bool joined, string? leftAlias)
    {
        if (!int.TryParse(cursor.Current.NumericText ?? cursor.Current.Text, NumberStyles.None,
            CultureInfo.InvariantCulture, out var ordinal) || ordinal < FirstOrdinal || ordinal > projection.Count)
        { throw SqlSyntax.Invalid(); }
        var selected = projection[ordinal - FirstOrdinal];
        if (selected.Path == SqlSyntax.Star)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, SqlSyntax.UnsupportedSyntaxDetail); }
        if (joined && !StringComparer.Ordinal.Equals(selected.SourceAlias, leftAlias))
        { throw SqlSyntax.Invalid(); }
        cursor.Advance();
        return selected.Path;
    }
}
