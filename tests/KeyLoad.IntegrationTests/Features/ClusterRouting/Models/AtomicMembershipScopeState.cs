namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record AtomicMembershipScopeState(
    RequestCqrsPhaseFaultIdentity Identity,
    AtomicPartitionPlacementResolution Placement,
    NodeStatus[] Owners,
    CommandRequest First,
    CommitReceipt FirstReceipt,
    CommandRequest Second,
    CommitReceipt SecondReceipt,
    OutboxStatus Outbox);
