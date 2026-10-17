namespace KeyLoad.Client;

internal static class QueryPredicateTokens
{
    internal const int FirstDepth = 1;
    internal const int DepthIncrement = 1;
    internal const int ParameterIndex = 0;
    internal const int MarkerArgumentCount = 1;
    internal const int CollectionArgumentIndex = 0;
    internal const int CandidateArgumentIndex = 1;
    internal const int ContainsArgumentCount = 2;
    internal const int EmptyCount = 0;
    internal const int MinimumQueryLimit = 1;
    internal const int MinimumArrayOrdinal = 0;
    internal const int ByteWidth = 8;
    internal const int ShortWidth = 16;
    internal const int IntWidth = 32;
    internal const int LongWidth = 64;
    internal const string PositiveLimitMessage = "The query limit must be positive.";
    internal const string MarkerExecutionMessage = "This marker is only supported inside a KeyLoad query expression.";
    internal const string PathSeparator = "/";
    internal const char PathSeparatorCharacter = '/';
    internal const string EscapeMarker = "~";
    internal const string EscapedMarker = "~0";
    internal const string EscapedSeparator = "~1";
    internal const string Wildcard = "*";
    internal const string IdentifierName = "id";
    internal const string RevisionName = "revision";

    internal const string UnsupportedExpressionMessage = "This expression is outside the supported Q1 C# subset.";
    internal const string PredicateDepthExceededMessage = "The C# query predicate exceeds its depth budget.";
    internal const string QueryPathDepthExceededMessage = "The C# query path exceeds its depth budget.";
    internal const string ConstantDepthExceededMessage = "The C# constant expression exceeds its depth budget.";
    internal const string ConstantArrayBudgetExceededMessage = "The C# constant array exceeds its item budget.";
    internal const string InListBudgetExceededMessage = "The C# IN list exceeds its budget.";

    internal const string ImplicitConversionOperatorName = "op_Implicit";
    internal const string NullableValueMemberName = nameof(Nullable<int>.Value);
    internal const string IdentifierPath = "/@id";
    internal const string RevisionPath = "/@revision";

    internal const string Equal = "=";
    internal const string NotEqual = "!=";
    internal const string GreaterThan = ">";
    internal const string GreaterThanOrEqual = ">=";
    internal const string LessThan = "<";
    internal const string LessThanOrEqual = "<=";
    internal const string And = "AND";
    internal const string Or = "OR";
}
