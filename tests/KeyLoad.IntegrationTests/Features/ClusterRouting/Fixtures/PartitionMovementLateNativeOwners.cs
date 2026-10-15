using KeyLoad.Clustering;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Server.Features.BlobStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Same six actual native production owners under one operation and one joined shutdown deadline.</summary>
internal sealed class PartitionMovementLateNativeOwners : IAsyncDisposable
{
    private const int FirstOwner = 0;
    private readonly ControlledPartitionMovementLoopbackListeners listeners = new();
    private readonly List<PartitionMovementLateNativeNode> nodes = [];
    internal PartitionMovementLateNativeSettings Settings
    {
        get;
    }
    internal PartitionMovementLateNativeBorrow Borrow { get; } = new();
    internal IReadOnlyList<PartitionMovementLateNativeNode> Nodes => nodes;
    private bool stopped = true;
    private Task? shutdown;
    private bool retainRoots;
    private CancellationTokenSource? shutdownDeadline;
    private readonly List<Task> producers = [];
    private TimeSpan? shutdownTimeout;
    private TimeProvider? shutdownClock;
    private readonly List<PartitionMovementLateNativeColdCohort> coldCohorts = [];
    private readonly IControlledBlobWireBorrowObserver? blobWireObserver;
    private readonly Action<int, List<string>>? configureArguments;
    private readonly Action<int, WebApplication>? configureApplication;

    internal PartitionMovementLateNativeOwners(IControlledBlobWireBorrowObserver? blobWireObserver = null,
        Action<int, List<string>>? configureArguments = null, Action<int, WebApplication>? configureApplication = null)
    {
        this.blobWireObserver = blobWireObserver;
        this.configureArguments = configureArguments;
        this.configureApplication = configureApplication;
        var root = Path.Combine(Path.GetTempPath(), "keyload-late-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Settings = new(listeners, root);
        listeners.JoinForChild();
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!stopped || producers.Any(task => !task.IsCompleted))
        {
            throw new InvalidOperationException("The previous native owners have not joined.");
        }
        shutdownDeadline?.Dispose();
        shutdownDeadline = null;
        nodes.Clear();
        shutdown = null;
        stopped = false;
        for (var index = FirstOwner; index < PartitionMovementLateNativeSettings.OwnerCount; index++)
        {
            nodes.Add(await PartitionMovementLateNativeNode.CreateAsync(Arguments(index),
                index < PartitionMovementLateNativeSettings.GroupSize ? Borrow.ForOwner(index) : null,
                index < PartitionMovementLateNativeSettings.GroupSize ? blobWireObserver : null,
                Settings.LocalhostClustering, cancellationToken).ConfigureAwait(false));
            configureApplication?.Invoke(index, nodes[index].Application);
        }
        shutdownTimeout = nodes.First().ShutdownTimeout;
        shutdownClock = nodes.First().Clock;
        await ControlledPartitionMovementNativeHosts.JoinOriginalAsync(
            Task.WhenAll(nodes.Select(node => node.StartAsync(cancellationToken)))).ConfigureAwait(false);
        var services = nodes.First().Application.Services;
        var runtime = services.GetRequiredService<ServerRuntimeOptions>();
        var clock = services.GetRequiredService<TimeProvider>();
        while (nodes.Any(node => !node.Silo.DatabaseReady))
        {
            await Task.Delay(runtime.Membership.Value.StartupRetryDelay, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private string[] Arguments(int index)
    {
        var original = Settings.Arguments(index);
        if (configureArguments is null)
        {
            return original;
        }
        var configured = original.ToList();
        configureArguments(index, configured);
        return configured.ToArray();
    }

    internal void TrackProducer(Task original) => producers.Add(original);
    internal void RetainRoots() => retainRoots = true;

    internal Task JoinProducerAsync(Task original)
    {
        if (shutdownDeadline is { } deadline)
        {
            return original.WaitAsync(deadline.Token);
        }
        if (original.IsCompleted)
        {
            return original;
        }
        throw new InvalidOperationException("The original native shutdown owner has no joined producer deadline.");
    }

    internal Task StopAsync() => shutdown ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        if (stopped)
        {
            return;
        }
        if (nodes.Count == FirstOwner)
        {
            stopped = true;
            return;
        }
        var first = nodes.First();
        shutdownDeadline ??= new CancellationTokenSource(shutdownTimeout ?? first.ShutdownTimeout,
            shutdownClock ?? first.Clock);
        var deadline = shutdownDeadline;
        var failures = new List<Exception>();
        foreach (var task in nodes.Select(node => node.StopAsync(deadline.Token)).ToArray())
        {
            await ServerFailureObserver.ObserveAsync(() => task, failures).ConfigureAwait(false);
        }
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

    internal async Task<PartitionMovementLateNativeNode> OpenStoppedAsync(int index, CancellationToken cancellationToken)
    {
        if (!stopped || coldCohorts.Any(cohort => !cohort.IsDisposed))
        {
            throw new InvalidOperationException("Native locks remain owned by the live cohort.");
        }
        var cohort = new PartitionMovementLateNativeColdCohort(Arguments, Settings.LocalhostClustering);
        coldCohorts.Add(cohort);
        return await cohort.OpenAsync(index, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(StopAsync, failures).ConfigureAwait(false);
        foreach (var cold in coldCohorts)
        {
            await ServerFailureObserver.ObserveAsync(() => cold.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        try
        {
            listeners.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        if (retainRoots || !stopped || nodes.Any(node => !node.IsDisposed) || coldCohorts.Any(cohort => !cohort.IsDisposed)
            || producers.Any(task => !task.IsCompleted))
        {
            failures.Add(new InvalidOperationException("Retain the native roots while any physical owner or original SDK producer is unjoined."));
        }
        if (stopped && nodes.All(node => node.IsDisposed) && coldCohorts.All(cohort => cohort.IsDisposed)
            && producers.All(task => task.IsCompleted))
        {
            ServerFailureObserver.Observe(() => shutdownDeadline?.Dispose(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        ServerFailureObserver.Observe(() => Directory.Delete(Settings.Root, recursive: true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
