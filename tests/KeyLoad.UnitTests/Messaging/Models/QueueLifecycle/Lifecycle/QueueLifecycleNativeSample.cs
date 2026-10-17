namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLifecycleNativeSample(ReplicatedOperation Operation, OperationResult Result,
    CommitToken ExpectedToken, IReadOnlyDictionary<string, MessageMetadata> Before, QueueCounters? Counters);
