namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record D2ResourceInventorySeed(
    ResourceLifetimeV1 A,
    ResourceLifetimeV1 B,
    ResourceLifetimeV1 C,
    RegisteredPhysicalOwnerV1 Observed,
    ReplicatedOperation[] Writes,
    OperationResult[] Results);
