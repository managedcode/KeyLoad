namespace KeyLoad.Query.QueryExecution;

/// <summary>Canonical Q1/Q2 syntax tokens, numeric limits, and stable error details.</summary>
internal static class SqlSyntax
{
    internal const string Events = "EVENTS";
    internal const string TopicEvents = "TOPIC_EVENTS";
    internal const string QueueMessages = "QUEUE_MESSAGES";
    internal const string And = "AND";
    internal const string Or = "OR";
    internal const string True = "TRUE";
    internal const string False = "FALSE";
    internal const string Unknown = "UNKNOWN";
    internal const string Star = "*";
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
    internal const string InvalidDetail = "The SQL statement is invalid for the supported dialect.";
    internal const string UnsupportedJoinDetail = "The query dialect does not support joins.";
    internal const int DefaultLimit = 100;

    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidDetail);
}
