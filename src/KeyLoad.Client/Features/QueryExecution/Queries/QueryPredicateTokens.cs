namespace KeyLoad.Client;

internal static class QueryPredicateTokens
{
    internal const int MaximumDepth = 32;
    internal const int MaximumInItems = 256;
    internal const int MaximumConstantArrayItems = 256;

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
