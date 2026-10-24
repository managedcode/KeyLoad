using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueuePendingAuthorityState(QueueLifecycleTestState Flow, Command<DatabaseCommand> Park,
    byte[] MarkerKey, byte[] MarkerBytes, EntityRefLifetime DocumentLifetime)
{
    internal List<QueuePendingAuthorityProcessed> Processed { get; } = [];
}
