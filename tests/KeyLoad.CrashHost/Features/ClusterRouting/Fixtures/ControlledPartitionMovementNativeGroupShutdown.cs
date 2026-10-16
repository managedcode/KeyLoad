using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeGroupShutdown
{
    private const int NativePrimary = 0;
    private const int FirstDependent = 1;
    private const int NoFailures = 0;

    internal static Task StopAsync(IReadOnlyList<ControlledPartitionMovementNativeHost> nodes,
        CancellationToken cancellationToken)
        => JoinAsync(nodes, static node => node.StopAsync(CancellationToken.None), cancellationToken);

    internal static Task DisposeAsync(IReadOnlyList<ControlledPartitionMovementNativeHost> nodes)
        => JoinAsync(nodes, static node => node.DisposeAsync().AsTask(), CancellationToken.None);

    private static async Task JoinAsync(IReadOnlyList<ControlledPartitionMovementNativeHost> nodes,
        Func<ControlledPartitionMovementNativeHost, Task> start, CancellationToken caller)
    {
        var failures = new List<Exception>();
        // Join each native directory handoff while every remaining transfer partner and the membership primary are live.
        for (var index = FirstDependent; index < nodes.Count; index++)
        {
            failures.AddRange(await JoinNodeAsync(nodes[index], start, index, caller).ConfigureAwait(false));
        }
        if (nodes.Count > NativePrimary)
        {
            failures.AddRange(await JoinNodeAsync(nodes[NativePrimary], start, NativePrimary, caller).ConfigureAwait(false));
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<List<Exception>> JoinNodeAsync(ControlledPartitionMovementNativeHost node,
        Func<ControlledPartitionMovementNativeHost, Task> start, int nodeIndex, CancellationToken caller)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            node.BindColdShutdownEvidence(nodeIndex);
            var original = start(node);
            var observation = new List<Exception>();
            await ControlledNativeColdShutdownEvidence.ObserveAsync(() => original, observation, nodeIndex,
                ControlledNativeColdShutdownEvidence.Stage.GroupObservation, true, caller, caller).ConfigureAwait(false);
            failures.AddRange(observation);
            if (observation.Count == NoFailures)
            {
                return;
            }
            var completion = new List<Exception>();
            await ControlledNativeColdShutdownEvidence.ObserveAsync(() => original, completion, nodeIndex,
                ControlledNativeColdShutdownEvidence.Stage.GroupOriginalJoin, false, caller, caller).ConfigureAwait(false);
            foreach (var failure in completion)
            {
                if (!observation.Any(observed => ReferenceEquals(observed, failure)))
                {
                    failures.Add(failure);
                }
            }
            await ControlledNativeColdShutdownEvidence.AfterJoinAsync(observation, original, failures, nodeIndex,
                ControlledNativeColdShutdownEvidence.Stage.GroupObservation).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        return failures;
    }
}
