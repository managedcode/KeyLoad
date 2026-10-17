namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed record LocalInboxProcessingRecoveryState(
    (ReplicatedOperation Operation, OperationResult Result)[] History,
    ReplicatedOperation Receive, OperationResult Received);
