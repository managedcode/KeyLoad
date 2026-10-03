namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlCommentTestData
{
    internal const string Collection = "orders";
    internal const string Principal = "root";
    internal const string Status = "status";
    internal const string StatusPath = "/status";
    internal const string Open = "open";
    internal const string Closed = "closed";
    internal const string DocumentA = "a";
    internal const string DocumentB = "b";
    internal const string OpenDocumentJson = "{\"status\":\"open\"}";
    internal const string ClosedDocumentJson = "{\"status\":\"closed\"}";
    internal const string QuotedDocumentJson = "{\"note/*field*/\":\"value -- text/* still value */\",\"note\":\"value -- text/* still value */\"}";
    internal const string NumericDocumentPrefix = "{\"number\":";
    internal const string NumericDocumentSuffix = "}";
    internal const string PlainSelect = "SELECT d.id FROM orders d WHERE d.status = 'open' ORDER BY d.id";
    internal const string CommentedSelect = "-- lead\r\nSELECT/* outer /* inner */ tail */ d.id FROM/* line\r\n*/orders d "
        + "WHERE d.status = 'open' ORDER BY d.id";
    internal const string EndOfInputLineCommentSelect = "SELECT d.id FROM orders d WHERE d.status = 'open' ORDER BY d.id"
        + "-- ignored through EOF";
    internal const string PlainExplain = "EXPLAIN SELECT * FROM orders WHERE status = 'open'";
    internal const string CommentedExplain = "EXPLAIN/* explain */ SELECT/* select */ * FROM orders "
        + "WHERE/* predicate */ status = 'open'";
    internal const string QuotedField = "note/*field*/";
    internal const string QuotedValue = "value -- text/* still value */";
    internal const string QuotedSql = "SELECT \"note/*field*/\" FROM orders WHERE note = 'value -- text/* still value */'";
    internal const string UnclosedComment = "SELECT * FROM orders /* unfinished";
    internal const string DeepComment = "SELECT/* outer /* middle /* inner */ */ */ * FROM orders";
    internal const string IdentifierSplice = "SEL/* gap */ECT * FROM orders";
    internal const string OperatorSplice = "SELECT * FROM orders WHERE n >/* gap */= 1";
    internal const string SecondStatement = "SELECT * FROM orders;/* hidden */ SELECT * FROM orders";
    internal const string ExactBoundedQuery = "SELECT/*å*/ * FROM orders";
    internal const string LongCommentPrefix = "/*";
    internal const string LongCommentSuffix = "*/";
    internal const string NumericLineCommentSql = "SELECT * FROM orders WHERE number = 3-- trailing\r\nLIMIT 1";
    internal const string NumericArithmeticLikeSql = "SELECT * FROM orders WHERE number = 3-4";
    internal const string NumberField = "number";
    internal const string NumberPath = "/number";
    internal const string NumberIndexPath = "index:number";
    internal const int NumberValue = 3;
    internal const string TokenBudgetQuery = "SELECT/* gap */ * FROM orders";
    internal const string IndexPath = "index:status";
    internal const int MaximumCommentDepth = 3;
    internal const int OverDepth = 1;
    internal const int ExactTokenCount = 4;
    internal const int ExcessTokenCount = 3;
    internal const int LongCommentSize = 8_192;
    internal const char LongCommentCharacter = 'x';
}
