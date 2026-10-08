using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Joins actual listener and both native store owners while retaining primary and every disposal failure.</summary>
internal static class ControlledPartitionMovementTerminalOwners
{
    internal static async Task ExecuteAsync(Func<ControlledPartitionMovementNode,
        ControlledPartitionMovementNode, ControlledPartitionMovementLoopbackListeners,
        ControlledPartitionMovementLoopbackCorpus, Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var listeners = new ControlledPartitionMovementLoopbackListeners();
            var corpus = new ControlledPartitionMovementLoopbackCorpus(listeners);
            await ServerFailureObserver.ObserveAsync(() => WithSourceAsync(listeners, corpus, operation), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithSourceAsync(ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus, Func<ControlledPartitionMovementNode,
        ControlledPartitionMovementNode, ControlledPartitionMovementLoopbackListeners,
        ControlledPartitionMovementLoopbackCorpus, Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new ControlledPartitionMovementNode(corpus.Control.Owner,
                ControlledPartitionMovementNativeJournal.TerminalHistoryEntries);
            await ServerFailureObserver.ObserveAsync(() => WithTargetAsync(source, listeners, corpus, operation), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithTargetAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementLoopbackCorpus corpus,
        Func<ControlledPartitionMovementNode, ControlledPartitionMovementNode,
        ControlledPartitionMovementLoopbackListeners, ControlledPartitionMovementLoopbackCorpus, Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var target = new ControlledPartitionMovementNode(corpus.Destination.Owner,
                ControlledPartitionMovementNativeJournal.TerminalHistoryEntries);
            await ServerFailureObserver.ObserveAsync(() => operation(source, target, listeners, corpus), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
