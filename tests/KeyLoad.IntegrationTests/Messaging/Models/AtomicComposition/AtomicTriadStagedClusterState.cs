namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record AtomicTriadStagedClusterState(PartitionRef Partition, MessagingClusterIdentity Identity, CommandRequest Failed)
{
    internal CommandRequest Healthy
    {
        get;
        set;
    } = null!;
    internal CommitReceipt Receipt
    {
        get;
        set;
    } = null!;
    internal OutboxStatus Outbox
    {
        get;
        set;
    } = new(new(0L, 1L, 0L, 0L), []);
    internal DateTimeOffset CommittedAt
    {
        get;
        set;
    }
    internal HashSet<Guid> Returned
    {
        get;
    } = [];
    internal bool HealthyCommitted
    {
        get;
        set;
    }
}
