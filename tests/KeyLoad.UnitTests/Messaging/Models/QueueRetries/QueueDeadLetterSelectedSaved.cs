namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeadLetterSelectedSaved(ReplicatedOperation Operation, OperationResult Result);
