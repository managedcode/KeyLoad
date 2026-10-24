
namespace KeyLoad.IntegrationTests.Authorization;

internal static class QueryOnlyEpochR46Protocol
{
    internal const string ScopeDetail = "The principal cannot perform this operation in this scope.";
    internal const string CursorDetail = "The query cursor no longer has a valid authorized read cut.";
    internal const string SafeScopeDetail = ScopeDetail;
    internal const string SafeCursorDetail = "The database operation could not be completed.";
    internal const string DetailKey = "detail";
    internal const string ColdScenario = "query-only-policy-epoch-cold";
}
