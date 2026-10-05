namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Independent JSON names for the accepted placement administration contracts.</summary>
internal static class McpPartitionPlacementCatalogProtocol
{
    internal const string Version = "version";
    internal const string ExpectedRevision = "expectedRevision";
    internal const string Partition = "partition";
    internal const string TenantId = "tenantId";
    internal const string DatabaseId = "databaseId";
    internal const string TransactionDomainId = "transactionDomainId";
    internal const string PartitionKey = "partitionKey";
    internal const string PhysicalShardId = "physicalShardId";
    internal const string Incarnation = "incarnation";
    internal const string VoterIds = "voterIds";
    internal const string PlacementEpoch = "placementEpoch";
    internal const string DirectoryRevision = "directoryRevision";
    internal const string Revision = "revision";
    internal const string IsFallback = "isFallback";
}
