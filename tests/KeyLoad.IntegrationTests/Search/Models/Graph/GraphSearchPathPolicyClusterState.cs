using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record GraphSearchPathPolicyClusterState(PartitionRef Partition, MessagingClusterIdentity Identity,
    CommandRequest Seed, CommitReceipt SeedReceipt, OutboxStatus Outbox,
    CommandRequest? Continuation = null, CommitReceipt? ContinuationReceipt = null)
{
    internal long MiddleRevision => Continuation is null ? 1L : 2L;
}
