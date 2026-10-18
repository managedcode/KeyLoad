
namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed record RecordedQueueFollowerOutcome(ReplicaEntry Entry, OperationResult Result, CommitToken ExpectedToken);
