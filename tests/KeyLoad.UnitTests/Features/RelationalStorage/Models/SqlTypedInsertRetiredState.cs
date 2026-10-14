using KeyLoad.UnitTests.Features.ClusterRouting;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed record SqlTypedInsertRetiredState(PrincipalRecord Principal, ReplicatedOperation Original,
    ReplicatedOperation Failed, CommitReceipt Receipt, ControlledPartitionMovementOutcomeAuthority Authority,
    ControlledPartitionMovementOutcomeAuthority FailedAuthority)
{
    internal bool EpochFenced { get; init; }
    internal ReplicatedOperation? Healthy { get; init; }
    internal CommitReceipt? HealthyReceipt { get; init; }
}
