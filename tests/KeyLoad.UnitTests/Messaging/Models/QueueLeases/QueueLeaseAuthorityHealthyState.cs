using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLeaseAuthorityHealthyState(CommandRequest Producer, Result<object> Produced,
    Command<DatabaseCommand> Claim, Result<object> Claimed, Command<DatabaseCommand> Ack, Result<object> Acked,
    QueueCounters Counters, MessageMetadata Metadata);
