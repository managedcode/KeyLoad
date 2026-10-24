namespace KeyLoad.IntegrationTests.Authorization;

/// <summary>Stable values for the persisted resource-policy Orleans scenario.</summary>
internal static class ResourcePolicyUpdateClusterProtocol
{
    internal const string Collection = "resource-policy-cluster-documents";
    internal const string DocumentId = "policy-document";
    internal const string SecretPath = "/secret";
    internal const string SecretValue = "resource-policy-cluster-private-value";
    internal const string SeedJson = "{\"name\":\"policy witness\",\"secret\":\"resource-policy-cluster-private-value\"}";
    internal const string Classification = "restricted";
    internal const string InitialReadGrant = "resource-policy.read.initial";
    internal const string InitialUseGrant = "resource-policy.use.initial";
    internal const string McpReadGrant = "resource-policy.read.after-mcp";
    internal const string McpUseGrant = "resource-policy.use.after-mcp";
    internal const string SdkReadGrant = "resource-policy.read.after-sdk";
    internal const string SdkUseGrant = "resource-policy.use.after-sdk";
    internal const string CurrentReadGrant = "resource-policy.read.current";
    internal const string CurrentUseGrant = "resource-policy.use.current";
    internal const string StaleGrant = "resource-policy.stale";
    internal const string QueryOperator = "=";
    internal const int QueryLimit = 10;
    internal const int InitialSchemaVersion = 1;
    internal const int AfterMcpUpdateSchemaVersion = 2;
    internal const int AfterSdkUpdateSchemaVersion = 3;
    internal const int CurrentSchemaVersion = 4;
}
