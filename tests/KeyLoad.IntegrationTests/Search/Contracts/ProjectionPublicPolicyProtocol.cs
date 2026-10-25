namespace KeyLoad.IntegrationTests.Search;

internal static class ProjectionPublicPolicyProtocol
{
    internal const string ColdScenario = "projection-public-policy-cold";
    internal const string ModelSql = """SELECT TOP (3) * FROM "event-projection-cluster" ORDER BY id""";
    internal const string Scan = "bounded-full-scan";
    internal const string BaselineJson = """{"kind":"baseline"}""";
    internal const string DerivedJson = """{"kind":"derived"}""";
    internal const string SourceJson = """{"input":"projection-source-private-canary"}""";
    private const double FirstScore = 1d / 61d;
    private const double SecondScore = 1d / 62d;
    internal static (string Id, double Score)[] Healthy()
        => [(EventProjectionClusterScenario.TargetId, FirstScore),
            (EventProjectionClusterScenario.BaselineId, SecondScore)];
    internal static readonly string[] Nodes =
        [ClientApi.McpCallerProtocol.Node1, ClientApi.McpCallerProtocol.Node2, ClientApi.McpCallerProtocol.Node3];
}
