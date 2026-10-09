using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Same six actual native production owners under one operation and one joined shutdown deadline.</summary>
internal sealed class PartitionMovementLateNativeOwners : IAsyncDisposable
{
    private const int FirstOwner = 0;
    private readonly ControlledPartitionMovementLoopbackListeners listeners = new();
    private readonly List<PartitionMovementLateNativeNode> nodes = [];
    internal PartitionMovementLateNativeSettings Settings { get; }
    internal PartitionMovementLateNativeBorrow Borrow { get; } = new();
    internal IReadOnlyList<PartitionMovementLateNativeNode> Nodes => nodes;
    private bool stopped = true;
    private Task? shutdown;
    private bool retainRoots;
    private CancellationTokenSource? shutdownDeadline;
    private readonly List<Task> producers = [];
    private TimeSpan? shutdownTimeout;
    private TimeProvider? shutdownClock;
    private readonly List<PartitionMovementLateNativeNode> coldNodes = [];

    internal PartitionMovementLateNativeOwners()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-late-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        Settings = new(listeners, root);
        listeners.JoinForChild();
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!stopped || producers.Any(task => !task.IsCompleted))
        { throw new InvalidOperationException("The previous native owners have not joined."); }
        shutdownDeadline?.Dispose();
        shutdownDeadline = null;
        nodes.Clear();
        shutdown = null;
        stopped = false;
        for (var index = FirstOwner; index < PartitionMovementLateNativeSettings.OwnerCount; index++)
        { nodes.Add(new(Settings.Arguments(index), index < PartitionMovementLateNativeSettings.GroupSize ? Borrow.ForOwner(index) : null)); }
        shutdownTimeout = nodes.First().ShutdownTimeout;
        shutdownClock = nodes.First().Clock;
        await Task.WhenAll(nodes.Select(node => node.StartAsync(cancellationToken))).ConfigureAwait(false);
        var services = nodes.First().Application.Services;
        var runtime = services.GetRequiredService<ServerRuntimeOptions>();
        var clock = services.GetRequiredService<TimeProvider>();
        while (nodes.Any(node => !node.Silo.DatabaseReady))
        { await Task.Delay(runtime.Membership.Value.StartupRetryDelay, clock, cancellationToken).ConfigureAwait(false); }
    }

    internal void TrackProducer(Task original) => producers.Add(original);
    internal void RetainRoots() => retainRoots = true;

    internal Task JoinProducerAsync(Task original)
    {
        if (shutdownDeadline is { } deadline)
        { return original.WaitAsync(deadline.Token); }
        if (original.IsCompleted)
        { return original; }
        throw new InvalidOperationException("The original native shutdown owner has no joined producer deadline.");
    }

    internal Task StopAsync() => shutdown ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        if (stopped)
        { return; }
        if (nodes.Count == FirstOwner)
        { stopped = true; return; }
        var first = nodes.First();
        shutdownDeadline ??= new CancellationTokenSource(shutdownTimeout ?? first.ShutdownTimeout,
            shutdownClock ?? first.Clock);
        var deadline = shutdownDeadline;
        var failures = new List<Exception>();
        foreach (var task in nodes.Select(node => node.StopAsync(deadline.Token)).ToArray())
        { await ServerFailureObserver.ObserveAsync(() => task, failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
        for (var index = FirstOwner; index < PartitionMovementLateNativeSettings.OwnerCount; index++)
        {
            var directory = Settings.DirectoryPath(index);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "replica", "owner.lock"));
        }
        stopped = true;
    }

    internal PartitionMovementLateNativeNode OpenStopped(int index)
    {
        if (!stopped)
        { throw new InvalidOperationException("Native locks remain owned by the live cohort."); }
        var owned = new PartitionMovementLateNativeNode(Settings.Arguments(index), null);
        coldNodes.Add(owned);
        return owned;
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(StopAsync, failures).ConfigureAwait(false);
        foreach (var cold in coldNodes)
        { await ServerFailureObserver.ObserveAsync(() => cold.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        try
        { listeners.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        if (retainRoots || !stopped || nodes.Any(node => !node.IsDisposed) || coldNodes.Any(node => !node.IsDisposed)
            || producers.Any(task => !task.IsCompleted))
        { failures.Add(new InvalidOperationException("Retain the native roots while any physical owner or original SDK producer is unjoined.")); }
        if (stopped && nodes.All(node => node.IsDisposed) && coldNodes.All(node => node.IsDisposed)
            && producers.All(task => task.IsCompleted))
        { ServerFailureObserver.Observe(() => shutdownDeadline?.Dispose(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        ServerFailureObserver.Observe(() => Directory.Delete(Settings.Root, recursive: true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
