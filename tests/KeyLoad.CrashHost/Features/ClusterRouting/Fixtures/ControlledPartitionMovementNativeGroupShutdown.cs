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
        var dependents = await Task.WhenAll(nodes.Skip(FirstDependent)
            .Select(node => JoinNodeAsync(node, start, caller))).ConfigureAwait(false);
        foreach (var original in dependents)
        { failures.AddRange(original); }
        if (nodes.Count > NativePrimary)
        { failures.AddRange(await JoinNodeAsync(nodes[NativePrimary], start, caller).ConfigureAwait(false)); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<List<Exception>> JoinNodeAsync(ControlledPartitionMovementNativeHost node,
        Func<ControlledPartitionMovementNativeHost, Task> start, CancellationToken caller)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var original = start(node);
            var observation = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => original.WaitAsync(caller), observation).ConfigureAwait(false);
            failures.AddRange(observation);
            if (observation.Count == NoFailures)
            { return; }
            var completion = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => original, completion).ConfigureAwait(false);
            foreach (var failure in completion)
            {
                if (!observation.Any(observed => ReferenceEquals(observed, failure)))
                { failures.Add(failure); }
            }
        }, failures).ConfigureAwait(false);
        return failures;
    }
}
