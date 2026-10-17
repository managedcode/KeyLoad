namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual fixture-issued public command and observed result retained for cold/epoch operation checks.</summary>
internal sealed record PartitionMovementControlledBlobCommandRf3Proof(OperationKind Kind, Guid CommandId,
    string Tool, object Request, object? Value, ErrorCode? Error, bool HasAuthority,
    Func<ErrorCode?, CancellationToken, Task> InvokeSdk, bool LifetimeRemoved = false);
