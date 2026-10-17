namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLifecycleNativeExpectation(CommitToken Token,
    IReadOnlyDictionary<string, MessageMetadata> Before, QueueCounters? Counters);
