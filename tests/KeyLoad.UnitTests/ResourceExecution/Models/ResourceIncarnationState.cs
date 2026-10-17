namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record ResourceIncarnationState(ReplicatedOperation Original, OperationResult Created,
    ResourceDefinition Definition, ResourceLifetimeV1 Lifetime, byte[] LifetimeBytes, byte[] CreationOutcomeBytes,
    ReplicatedOperation Updated, OperationResult UpdatedResult,
    List<(ReplicatedOperation Command, OperationResult Result)> Outcomes);
