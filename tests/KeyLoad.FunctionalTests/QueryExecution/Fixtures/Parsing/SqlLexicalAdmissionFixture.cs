namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlLexicalAdmissionFixture
{
    internal const string Kind = SqlJoinSourceBindingFixture.Typed;
    private const string Select = "SELECT";
    private const string From = "FROM";
    private const string Semicolon = ";";
    private const string Where = "WHERE l.total = 7 AND l.quantity = 1 ";
    private const string Order = "ORDER BY l.order_id ASC";
    private const string Comparison = "WHERE l.total '=' 7 ";
    private const string Identifier = "/order_id";
    private const string OrderId = "order";
    private const string And = "AND";
    private const string Equal = "=";
    private const string Null = "NULL";
    private const string True = "TRUE";
    private const string QuotedSelect = "'SELECT'";
    private const string ParameterSelect = "@SELECT";
    private const string QuotedFrom = "'FROM'";
    private const string AndToken = " AND ";
    private const string QuotedAnd = " 'AND' ";
    private const string ParameterAnd = " @AND ";
    private const string Quote = "'";
    private const string LiteralPredicate = "WHERE l.order_id IN ('order', 'SELECT', 'FROM', 'AND', '=', 'NULL', 'TRUE') ";

    internal static IEnumerable<(string Sql, global::KeyLoad.ErrorCode Code)> Faults(global::KeyLoad.QueryRequest healthy)
    {
        yield return (healthy.Sql.Replace(Select, QuotedSelect, StringComparison.Ordinal), global::KeyLoad.ErrorCode.Validation);
        yield return (healthy.Sql.Replace(Select, ParameterSelect, StringComparison.Ordinal), global::KeyLoad.ErrorCode.Validation);
        yield return (healthy.Sql.Replace(From, QuotedFrom, StringComparison.Ordinal), global::KeyLoad.ErrorCode.Validation);
        yield return (healthy.Sql.Replace(Order, Comparison + Order, StringComparison.Ordinal), global::KeyLoad.ErrorCode.Validation);
        var predicate = healthy.Sql.Replace(Order, Where + Order, StringComparison.Ordinal);
        yield return (predicate.Replace(AndToken, QuotedAnd, StringComparison.Ordinal), global::KeyLoad.ErrorCode.Validation);
        yield return (predicate.Replace(AndToken, ParameterAnd, StringComparison.Ordinal), global::KeyLoad.ErrorCode.Validation);
        yield return (healthy.Sql + Quote + Semicolon + Quote, global::KeyLoad.ErrorCode.Validation);
    }

    internal static global::KeyLoad.QueryRequest LiteralSql(global::KeyLoad.QueryRequest healthy)
        => healthy with
        {
            Sql = healthy.Sql.Replace(Order, LiteralPredicate + Order, StringComparison.Ordinal)
        };

    internal static global::KeyLoad.Query.AstQueryRequest LiteralAst(global::KeyLoad.Query.AstQueryRequest healthy)
        => healthy with
        {
            Query = healthy.Query with
            {
                Filter = new global::KeyLoad.Query.InPredicate(
            new global::KeyLoad.Query.FieldOperand(Identifier),
            [global::KeyLoad.Query.ValueOperand.Create(OrderId), global::KeyLoad.Query.ValueOperand.Create(Select),
             global::KeyLoad.Query.ValueOperand.Create(From), global::KeyLoad.Query.ValueOperand.Create(And),
             global::KeyLoad.Query.ValueOperand.Create(Equal), global::KeyLoad.Query.ValueOperand.Create(Null),
             global::KeyLoad.Query.ValueOperand.Create(True)], false)
            }
        };
}
