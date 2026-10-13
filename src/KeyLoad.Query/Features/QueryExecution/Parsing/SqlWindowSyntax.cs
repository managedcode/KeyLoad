namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlWindowSyntax
{
    private const int InitialOffset = 0;
    private const int MinimumCount = 1;

    internal static (int Limit, int Offset) Read(SqlTokenCursor cursor)
    {
        var limit = SqlSyntax.DefaultLimit;
        var offset = InitialOffset;
        if (cursor.Eat(SqlSyntax.Fetch))
        { return (Fetch(cursor), InitialOffset); }
        if (cursor.Eat(SqlSyntax.Limit))
        {
            limit = Count(cursor);
            if (cursor.Eat(SqlSyntax.Offset))
            {
                offset = Offset(cursor);
            }
        }
        else if (cursor.Eat(SqlSyntax.Offset))
        {
            offset = Offset(cursor);
            limit = CountAfterOffset(cursor, limit);
        }
        return (limit, offset);
    }

    private static int CountAfterOffset(SqlTokenCursor cursor, int defaultLimit)
    {
        if (cursor.Eat(SqlSyntax.Limit))
        { return Count(cursor); }
        return cursor.Eat(SqlSyntax.Fetch) ? Fetch(cursor) : defaultLimit;
    }

    private static int Fetch(SqlTokenCursor cursor)
    {
        if (!cursor.Eat(SqlSyntax.First))
        { cursor.Need(SqlSyntax.Next); }
        var count = cursor.Is(SqlSyntax.Row) || cursor.Is(SqlSyntax.Rows)
            ? MinimumCount : Count(cursor);
        if (!cursor.Eat(SqlSyntax.Row))
        { cursor.Need(SqlSyntax.Rows); }
        if (cursor.Is(SqlSyntax.With))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, SqlSyntax.UnsupportedSyntaxDetail); }
        cursor.Need(SqlSyntax.Only);
        return count;
    }

    private static int Offset(SqlTokenCursor cursor)
    {
        var value = Count(cursor);
        if (!cursor.Eat(SqlSyntax.Row))
        {
            cursor.Eat(SqlSyntax.Rows);
        }
        return value;
    }

    private static int Count(SqlTokenCursor cursor)
    {
        if (cursor.Current.Kind != SqlTokenKind.Number
            || !int.TryParse(cursor.Current.Text, global::System.Globalization.NumberStyles.None,
                global::System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw SqlSyntax.Invalid();
        }
        cursor.Advance();
        return value;
    }
}
