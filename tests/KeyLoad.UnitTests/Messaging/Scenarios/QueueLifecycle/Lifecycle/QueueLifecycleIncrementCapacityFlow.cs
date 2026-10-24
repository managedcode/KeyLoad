namespace KeyLoad.UnitTests.Messaging;

internal static class QueueLifecycleIncrementCapacityFlow
{
    internal static Task RunAsync(QueueLifecycleCeilingFault fault, CancellationToken token)
        => QueueLifecycleCeilingInjection.IsPark(fault)
            ? QueuePendingIncrementCapacityFlow.RunAsync(fault, token)
            : QueueOrderedIncrementCapacityFlow.RunAsync(fault, token);
}
