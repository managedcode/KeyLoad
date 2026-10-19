namespace KeyLoad.IntegrationTests.Features.Authorization;

/// <summary>Stable values for the persisted resource-policy Orleans scenario.</summary>
internal static class ResourcePolicyUpdateClusterProtocol
{
    internal const string Collection = "resource-policy-cluster-documents";
    internal const string DocumentId = "policy-document";
    internal const string SecretPath = "/secret";
    internal const string SecretValue = "resource-policy-cluster-private-value";
    internal const string SeedJson = "{\"name\":\"policy witness\",\"secret\":\"resource-policy-cluster-private-value\"}";
    internal const string Classification = "restricted";
    internal const string ReadGrant = "resource-policy.read";
    internal const string UseGrant = "resource-policy.use";
    internal const string ReadGrant = "resource-policy.read";
    internal const string UseGrant = "resource-policy.use";
    internal const string ReadGrant = "resource-policy.read";
    internal const string UseGrant = "resource-policy.use";
    internal const string ReadGrant = "resource-policy.read";
    internal const string UseGrant = "resource-policy.use";
    internal const string StaleGrant = "resource-policy.stale";
    internal const string QueryOperator = "=";
    internal const int QueryLimit = 10;
    internal const int InitialSchemaVersion = 1;
    internal const int AfterMcpUpdateSchemaVersion = 2;
    internal const int AfterSdkUpdateSchemaVersion = 3;
    internal const int CurrentSchemaVersion = 4;
}
