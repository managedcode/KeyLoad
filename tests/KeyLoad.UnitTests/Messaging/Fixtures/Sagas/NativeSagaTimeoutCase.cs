using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record NativeSagaTimeoutCase(QueueLaneRef Lane, QueueLaneRef TimeoutLane, Guid Id,
    DateTimeOffset Deadline, DueWorkHint Hint);
