namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueLifecycleExpectation(CommitToken Token,
    IReadOnlyDictionary<string, MessageMetadata> Before, QueueCounters? Counters);
