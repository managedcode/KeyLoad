namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochPublicProtocol
{
    internal const string PutKind = "putDocument";
    internal const int RouteCount = 4;
    internal const int Sdk = 0;
    internal const int Mcp = 1;
    internal const int Q1Sdk = 2;
    internal const int QueryLimit = 1;
    internal const int CompleteLimit = 2;
    internal const long SeedRevision = 1;
    internal const string SecondId = "policy-page-witness";
    internal const string SecondJson = "{\"name\":\"page witness\",\"secret\":\"resource-policy-rf3-private-value\"}";
    internal const string FirstSafeJson = "{\"name\":\"policy witness\"}";
    internal const string SecondSafeJson = "{\"name\":\"page witness\"}";
    internal const string Scan = "bounded-full-scan";
    internal const string ColdScenario = "policy-epoch-public-cursor-cold";
    internal static readonly string[] Nodes = [ClientApi.McpCallerProtocol.Node1,
        ClientApi.McpCallerProtocol.Node2, ClientApi.McpCallerProtocol.Node3];
    internal static QueryRequest Request(PartitionRef partition, string? cursor = null)
        => new(partition, "SELECT * FROM \"" + ResourcePolicyUpdateRf3Protocol.Collection + "\"" + " ORDER BY id LIMIT 1",
            AllowFullScan: true, Cursor: cursor);
    internal static QueryRequest Complete(PartitionRef partition)
        => new(partition, "SELECT * FROM \"" + ResourcePolicyUpdateRf3Protocol.Collection + "\"" + " ORDER BY id LIMIT 2",
            AllowFullScan: true);
}
