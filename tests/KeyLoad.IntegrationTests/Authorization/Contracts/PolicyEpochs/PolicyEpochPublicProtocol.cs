using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class PolicyEpochPublicProtocol
{
    internal const string PutKind = "putDocument";
    private const string AstStar = "*";
    private const string AstMetadataIdPath = "/@id";
    internal const int RouteCount = 6;
    internal const int Sdk = 0;
    internal const int Mcp = 1;
    internal const int Q1Sdk = 2;
    internal const int SdkAst = 4;
    internal const int McpAst = 5;
    internal const int QueryLimit = 1;
    internal const int CompleteLimit = 2;
    internal const long SeedRevision = 1;
    internal const string SecondId = "policy-page-witness";
    internal const string SecondJson = "{\"name\":\"page witness\",\"secret\":\"resource-policy-cluster-private-value\"}";
    internal const string FirstSafeJson = "{\"name\":\"policy witness\"}";
    internal const string SecondSafeJson = "{\"name\":\"page witness\"}";
    internal const string Scan = "bounded-full-scan";
    internal const string ColdScenario = "policy-epoch-public-cursor-cold";
    internal static readonly string[] Nodes = [ClientApi.McpCallerProtocol.Node1,
        ClientApi.McpCallerProtocol.Node2, ClientApi.McpCallerProtocol.Node3];
    internal static QueryRequest Request(PartitionRef partition, string? cursor = null)
        => new(partition, "SELECT TOP (1) * FROM \"" + ResourcePolicyUpdateClusterProtocol.Collection + "\"" + " ORDER BY id",
            AllowFullScan: true, Cursor: cursor);
    internal static QueryRequest Complete(PartitionRef partition)
        => new(partition, "SELECT TOP (2) * FROM \"" + ResourcePolicyUpdateClusterProtocol.Collection + "\"" + " ORDER BY id",
            AllowFullScan: true);
    internal static AstQueryRequest Ast(PartitionRef partition, string? cursor = null)
        => new(partition, new SelectQuery(ResourcePolicyUpdateClusterProtocol.Collection, null,
            [new(AstStar,
                AstStar)], null,
            [new(AstMetadataIdPath, false)], QueryLimit),
            AllowFullScan: true, Cursor: cursor);
}
