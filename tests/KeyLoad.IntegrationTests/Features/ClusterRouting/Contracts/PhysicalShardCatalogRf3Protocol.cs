namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogRf3Protocol
{
    internal const string RestartScenario = "physical-shard-catalog-homogeneous-restart";
    internal const string DeniedWriteDocumentPrefix = "catalog-denied-";
    internal const string DeniedWriteJson = "{\"mustNotCommit\":true}";
    internal const string ReadyStatusField = "status";
    internal const string ReadyStatusValue = "ready";
    internal const string ReadyVotersField = "voters";
    internal const int VoterCount = 3;
}
