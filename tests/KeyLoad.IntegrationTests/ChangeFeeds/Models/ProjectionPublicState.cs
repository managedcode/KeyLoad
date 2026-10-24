using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal sealed record ProjectionPublicState(PartitionRef Partition, McpPersistedIdentity Identity,
    CommandRequest Source, CommitReceipt SourceReceipt, ProjectionConsumerRef Consumer,
    ProjectionConsumerDefinition Definition, ConfigureProjectionConsumerRequest Configure,
    ProjectionConsumerInfo Configured, ProjectionBatch OriginalBatch,
    CommitProjectionBatchRequest Complete, ProjectionBatchResult Completed,
    CommitProjectionBatchRequest Filtered, ProjectionBatchResult Advanced);
