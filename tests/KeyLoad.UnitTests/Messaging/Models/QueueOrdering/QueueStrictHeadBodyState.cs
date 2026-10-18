using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueStrictHeadBodyState(QueueOrderedRetryState Queue, MessageState HeadState,
    DateTimeOffset ScheduledAt, byte[] BodyKey, byte[] OriginalBody, Command<DatabaseCommand> BlockedReceive,
    QueueLifecycleConditionCut Original, QueueLifecycleConditionCut Damaged);
