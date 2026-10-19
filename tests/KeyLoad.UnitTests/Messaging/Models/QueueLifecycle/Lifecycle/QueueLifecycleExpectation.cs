namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLifecycleExpectation(CommitToken Token,
    IReadOnlyDictionary<string, MessageMetadata> Before, QueueCounters? Counters);
