using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record QueueAccountingFaultState(QueueLaneRef Lane, DateTimeOffset Start,
    EnqueueMessage Message, CommandRequest Producer, Result<object> Produced,
    Command<DatabaseCommand> Operation, QueueAccountingOperation Kind, QueueCounters Counters,
    MessageMetadata Metadata, long BodyBytes);
