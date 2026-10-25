using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.ChangeFeeds;

internal sealed record ProjectionPublicState(PartitionRef Partition, McpPersistedIdentity Identity,
    CommandRequest Source, CommitReceipt SourceReceipt, ProjectionConsumerRef Consumer,
    ProjectionConsumerDefinition Definition, ConfigureProjectionConsumerRequest Configure,
    ProjectionConsumerInfo Configured, ProjectionBatch OriginalBatch,
    CommitProjectionBatchRequest Complete, ProjectionBatchResult Completed,
    CommitProjectionBatchRequest Filtered, ProjectionBatchResult Advanced,
    EntityRefLifetime SourceLifetime, EntityRefLifetime TargetLifetime)
{
    internal EntityRef StoredSource(string id) => new(Partition, ProjectionPublicProtocol.Source, id) { Lifetime = SourceLifetime };
    internal EntityRef StoredTarget(string id) => new(Partition, ProjectionPublicProtocol.Target, id) { Lifetime = TargetLifetime };
}
