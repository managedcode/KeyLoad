using KeyLoad.Core.Messaging;

namespace KeyLoad.UnitTests.Messaging;

internal sealed record SagaTimeoutCase(QueueLaneRef Lane, QueueLaneRef TimeoutLane, Guid Id,
    DateTimeOffset Deadline, DueWorkHint Hint);
