namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueuePendingAuthorityState(QueueLifecycleTestState Flow, ReplicatedOperation Park,
    byte[] MarkerKey, byte[] MarkerBytes)
{
    internal List<QueuePendingAuthorityProcessed> Processed { get; } = [];
}
