using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record D2ArchiveSource(
    int Ordinal,
    ControlledPartitionMovementNode Owner,
    RegisteredPhysicalOwnerV1 Registered,
    PartitionRef Partition,
    ResourceLifetimeV1 DocumentsLifetime,
    ResourceLifetimeV1 UnrelatedLifetime,
    ReplicatedOperation Original,
    ReplicatedOperation NativeOriginal,
    OperationResult Outcome,
    byte[] RetainedOutcome,
    bool Unknown);
