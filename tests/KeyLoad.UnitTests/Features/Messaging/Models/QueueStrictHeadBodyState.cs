namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueStrictHeadBodyState(QueueOrderedRetryState Queue, MessageState HeadState,
    DateTimeOffset ScheduledAt, byte[] BodyKey, byte[] OriginalBody, ReplicatedOperation BlockedReceive,
    QueueLifecycleConditionCut Original, QueueLifecycleConditionCut Damaged);
