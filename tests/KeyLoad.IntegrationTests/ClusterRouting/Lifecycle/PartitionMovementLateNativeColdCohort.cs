using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns all six native-active applications for one actual cold inspection lease.</summary>
internal sealed class PartitionMovementLateNativeColdCohort(Func<int, string[]> arguments,
    NativeLocalhostClusteringOptions localhost) : IAsyncDisposable
{
    private const int NativePrimary = 0;
    private const int FirstDependent = 1;
    private readonly List<PartitionMovementLateNativeNode> nodes = [];
    private readonly Lock lifecycle = new();
    private Task? initialization;
    private Task? shutdown;
    private bool closed;
    internal bool IsDisposed => Volatile.Read(ref closed);

    internal async Task<PartitionMovementLateNativeNode> OpenAsync(int selectedOwner, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(selectedOwner);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(selectedOwner, PartitionMovementLateNativeSettings.OwnerCount);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            initialization ??= InitializeAsync(cancellationToken);
        }
        await initialization.ConfigureAwait(false);
        var selected = nodes[selectedOwner];
        selected.BindColdCohort(StopAsync);
        return selected;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        for (var index = NativePrimary; index < PartitionMovementLateNativeSettings.OwnerCount; index++)
        {
            nodes.Add(await PartitionMovementLateNativeNode.CreateAsync(arguments(index), null,
                localhostClustering: localhost, startupCancellation: cancellationToken).ConfigureAwait(false));
        }
        await ControlledPartitionMovementNativeHosts.JoinOriginalAsync(
            Task.WhenAll(nodes.Select(node => node.OpenAsync(cancellationToken)))).ConfigureAwait(false);
    }

    private Task StopAsync(CancellationToken cancellationToken)
    {
        Task original;
        lock (lifecycle)
        {
            original = shutdown ??= StopCoreAsync();
        }
        return PartitionMovementLateNativeNode.JoinShutdownAsync(original, cancellationToken);
    }

    private async Task StopCoreAsync()
    {
        var failures = new List<Exception>();
        if (initialization is { } opening)
        {
            await opening.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        var dependents = nodes.Skip(FirstDependent).Select(node => node.StopLocalAsync(CancellationToken.None)).ToArray();
        foreach (var original in dependents)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
        }
        if (nodes.Count > NativePrimary)
        {
            await ServerFailureObserver.ObserveAsync(() => nodes[NativePrimary].StopLocalAsync(CancellationToken.None), failures).ConfigureAwait(false);
        }
        if (nodes.Any(node => !node.IsDisposed))
        {
            failures.Add(new InvalidOperationException("The original cold native cohort has unjoined physical owners."));
        }
        ServerFailureObserver.ThrowIfAny(failures);
        PartitionMovementLateNativeColdLocks.RequireReleased(nodes);
        Volatile.Write(ref closed, true);
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);
}
