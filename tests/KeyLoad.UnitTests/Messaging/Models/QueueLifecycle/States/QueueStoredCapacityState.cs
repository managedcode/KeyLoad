using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueStoredCapacityState(CommandRequest Producer, global::ManagedCode.Communication.Result<object> Original,
    DateTimeOffset ScheduledAt, global::ManagedCode.Communication.Commands.Command<DatabaseCommand> Enqueue, global::ManagedCode.Communication.Commands.Command<DatabaseCommand> Receive);
