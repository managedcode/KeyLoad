using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueAccountingFaultState(QueueLaneRef Lane, DateTimeOffset Start,
    EnqueueMessage Message, CommandRequest Producer, Result<object> Produced,
    Command<DatabaseCommand> Operation, QueueAccountingOperation Kind, QueueCounters Counters,
    MessageMetadata Metadata, long BodyBytes);
