namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlBooleanOperandPredicate
{
    internal static bool IsBoundary(SqlTokenCursor cursor)
        => cursor.Current.Kind == SqlTokenKind.End || cursor.Is(SqlSyntax.CloseParen)
            || cursor.Is(SqlSyntax.And) || cursor.Is(SqlSyntax.Or) || cursor.Is(SqlSyntax.Order)
            || cursor.Is(SqlSyntax.Limit) || cursor.Is(SqlSyntax.Offset) || cursor.Is(SqlSyntax.Fetch)
            || cursor.Is(SqlSyntax.Semicolon);

    internal static Predicate Lower(Operand value, SqlTokenKind originalKind)
    {
        if (originalKind is SqlTokenKind.Number or SqlTokenKind.String)
        { throw SqlSyntax.Invalid(); }
        return new Comparison(value, SqlSyntax.Equals, ValueOperand.Create(true));
    }
}
