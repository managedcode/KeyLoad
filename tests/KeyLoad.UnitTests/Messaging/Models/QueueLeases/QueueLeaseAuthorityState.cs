using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueLeaseAuthorityState(QueueLaneRef Lane, DateTimeOffset Start,
    EnqueueMessage Message, CommandRequest Producer, Result<object> Produced,
    Command<DatabaseCommand> Claim, Result<object> Claimed, Delivery Delivery,
    Command<DatabaseCommand> Original, QueueLeaseAuthorityOperation Kind, MessageMetadata Metadata,
    QueueCounters Counters, long BodyBytes, byte[] FaultKey, byte[] OriginalBytes, EntityRef DocumentReference);
