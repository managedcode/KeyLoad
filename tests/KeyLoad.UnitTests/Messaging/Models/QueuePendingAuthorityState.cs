using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueuePendingAuthorityState(QueueLifecycleTestState Flow, Command<DatabaseCommand> Park,
    byte[] MarkerKey, byte[] MarkerBytes)
{
    internal List<QueuePendingAuthorityProcessed> Processed { get; } = [];
}
