using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record GraphSearchPathPolicyRf3State(PartitionRef Partition, MessagingRf3Identity Identity,
    CommandRequest Seed, CommitReceipt SeedReceipt, OutboxStatus Outbox,
    CommandRequest? Continuation = null, CommitReceipt? ContinuationReceipt = null)
{
    internal long MiddleRevision => Continuation is null ? 1L : 2L;
}
