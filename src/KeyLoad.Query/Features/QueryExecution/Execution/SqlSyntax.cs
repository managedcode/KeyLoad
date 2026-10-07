namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Canonical Q1/Q2 syntax tokens, numeric limits, and stable error details.</summary>
internal static class SqlSyntax
{
    internal const string Explain = "EXPLAIN";
    internal const string Select = "SELECT";
    internal const string From = "FROM";
    internal const string Inner = "INNER";
    internal const string Join = "JOIN";
    internal const string On = "ON";
    internal const string Events = "EVENTS";
    internal const string QueueMessages = "QUEUE_MESSAGES";
    internal const string As = "AS";
    internal const string Where = "WHERE";
    internal const string Order = "ORDER";
    internal const string By = "BY";
    internal const string Desc = "DESC";
    internal const string Asc = "ASC";
    internal const string Limit = "LIMIT";
    internal const string Not = "NOT";
    internal const string Is = "IS";
    internal const string Missing = "MISSING";
    internal const string Null = "NULL";
    internal const string In = "IN";
    internal const string Between = "BETWEEN";
    internal const string And = "AND";
    internal const string Or = "OR";
    internal const string True = "TRUE";
    internal const string False = "FALSE";
    internal const string Star = "*";
    internal const string Comma = ",";
    internal const string Dot = ".";
    internal const string Semicolon = ";";
    internal const string OpenParen = "(";
    internal const string CloseParen = ")";
    internal new const string Equals = "=";
    internal const string NotEquals = "!=";
    internal const string AlternateNotEquals = "<>";
    internal const string Greater = ">";
    internal const string GreaterOrEqual = ">=";
    internal const string Less = "<";
    internal const string LessOrEqual = "<=";
    internal const string MetadataId = "id";
    internal const string MetadataRevision = "revision";
    internal const string MetadataPrefix = "/@";
    internal const string MetadataIdPath = MetadataPrefix + MetadataId;
    internal const string MetadataRevisionPath = MetadataPrefix + MetadataRevision;
    internal const string ByteBudgetDetail = "The SQL byte budget is exceeded.";
    internal const string TokenBudgetDetail = "The SQL token budget is exceeded.";
    internal const string DepthBudgetDetail = "The SQL depth budget is exceeded.";
    internal const string UnsupportedSyntaxDetail = "The SQL statement contains unsupported syntax.";
    internal const string InvalidDetail = "The SQL statement is invalid for the supported Q1 dialect.";
    internal const string UnsupportedDialectDetail = "The selected query dialect is unsupported.";
    internal const string UnsupportedJoinDetail = "The query dialect does not support joins.";
    internal const int DefaultLimit = 100;

    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidDetail);
}
