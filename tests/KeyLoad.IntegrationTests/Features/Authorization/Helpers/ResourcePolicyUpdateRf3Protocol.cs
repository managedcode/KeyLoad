namespace KeyLoad.IntegrationTests.Features.Authorization;

/// <summary>Stable values for the persisted resource-policy RF3 scenario.</summary>
internal static class ResourcePolicyUpdateRf3Protocol
{
    internal const string Collection = "resource-policy-rf3-documents";
    internal const string DocumentId = "policy-document";
    internal const string SecretPath = "/secret";
    internal const string SecretValue = "resource-policy-rf3-private-value";
    internal const string SeedJson = "{\"name\":\"policy witness\",\"secret\":\"resource-policy-rf3-private-value\"}";
    internal const string Classification = "restricted";
    internal const string ReadGrantV1 = "resource-policy.read.v1";
    internal const string UseGrantV1 = "resource-policy.use.v1";
    internal const string ReadGrantV2 = "resource-policy.read.v2";
    internal const string UseGrantV2 = "resource-policy.use.v2";
    internal const string ReadGrantV3 = "resource-policy.read.v3";
    internal const string UseGrantV3 = "resource-policy.use.v3";
    internal const string ReadGrantV4 = "resource-policy.read.v4";
    internal const string UseGrantV4 = "resource-policy.use.v4";
    internal const string StaleGrant = "resource-policy.stale";
    internal const string QueryOperator = "=";
    internal const int QueryLimit = 10;
    internal const int InitialSchemaVersion = 1;
    internal const int AfterMcpUpdateSchemaVersion = 2;
    internal const int AfterSdkUpdateSchemaVersion = 3;
    internal const int CurrentSchemaVersion = 4;
}
