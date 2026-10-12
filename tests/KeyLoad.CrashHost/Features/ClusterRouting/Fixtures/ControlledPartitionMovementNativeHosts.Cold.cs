using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHosts
{
    internal Task RestartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            if (restart is { IsCompleted: false })
            { throw new InvalidOperationException(nameof(RestartAsync)); }
            var originalStop = stopped;
            stopped = null;
            return restart = RestartCoreAsync(originalStop, cancellationToken);
        }
    }

    private async Task RestartCoreAsync(Task? originalStop, CancellationToken cancellationToken)
    {
        if (originalStop is not null) { await JoinOriginalAsync(originalStop).ConfigureAwait(false); }
        await JoinOriginalAsync(Task.WhenAll(nodes.Select(node => node.StopAsync(cancellationToken)))).ConfigureAwait(false);
        if (nodes.Any(node => !node.IsJoined))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        nodes = [];
        await CreateNodesAsync().ConfigureAwait(false);
        await JoinOriginalAsync(Task.WhenAll(nodes.Select(node => node.StartAsync(cancellationToken)))).ConfigureAwait(false);
    }

    internal void RetainRoots()
    {
        lock (lifecycle) { Volatile.Write(ref retainRoots, true); }
    }

    internal Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            return stopped ??= StopCoreAsync(restart, cancellationToken);
        }
    }

    private async Task StopCoreAsync(Task? originalRestart, CancellationToken cancellationToken)
    {
        if (originalRestart is not null) { await JoinOriginalAsync(originalRestart).ConfigureAwait(false); }
        await JoinOriginalAsync(Task.WhenAll(nodes.Select(node => node.StopAsync(cancellationToken)))).ConfigureAwait(false);
        if (nodes.Count != ControlledPartitionMovementNativeHostEndpoints.OwnerCount || nodes.Any(node => !node.IsJoined))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        { return new(shutdown ??= CloseAsync(restart, stopped, retainRoot: false)); }
    }

    internal ValueTask CloseRetainingRootAsync()
    {
        lock (lifecycle)
        { return new(shutdown ??= CloseAsync(restart, stopped, retainRoot: true)); }
    }

    private async Task CloseAsync(Task? originalRestart, Task? originalStop, bool retainRoot)
    {
        var failures = new List<Exception>();
        if (originalRestart is not null)
        { await ServerFailureObserver.ObserveAsync(() => originalRestart, failures).ConfigureAwait(false); }
        if (originalStop is not null)
        { await ServerFailureObserver.ObserveAsync(() => originalStop, failures).ConfigureAwait(false); }
        await ServerFailureObserver.ObserveAsync(
            () => JoinOriginalAsync(Task.WhenAll(nodes.Select(node => node.DisposeAsync().AsTask()))),
            failures).ConfigureAwait(false);
        if (nodes.Count != ControlledPartitionMovementNativeHostEndpoints.OwnerCount
            || nodes.Any(node => !node.IsJoined))
        { failures.Add(Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable)); }
        if (failures.Count == 0 && creationFailure is null && !retainRoot && !Volatile.Read(ref retainRoots))
        { ServerFailureObserver.Observe(() => Directory.Delete(settings.Root, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
