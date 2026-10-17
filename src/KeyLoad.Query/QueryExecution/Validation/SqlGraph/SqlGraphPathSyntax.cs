namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Stable tokens, bounds and diagnostics for the closed Q1.GraphPath.v1 grammar.</summary>
internal static class SqlGraphPathSyntax
{
    internal const string Function = "GRAPH_SHORTEST_PATH";
    internal const string InvalidDetail = "The SQL graph-path statement is invalid for Q1.GraphPath.v1.";
    internal const string WrapperDetail = "The SQL graph-path request is invalid or exceeds its configured bound.";
    internal const string ParameterDetail = "A named SQL graph-path parameter is missing or has the wrong scalar type.";
    internal const string ParameterBudgetDetail = "The SQL graph-path parameter budget is exceeded.";
    internal const string VersionProfile = "graph-path-v1";
    internal const int Version = 1;
    internal const int MandatoryArgumentCount = 8;

    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidDetail);
}
