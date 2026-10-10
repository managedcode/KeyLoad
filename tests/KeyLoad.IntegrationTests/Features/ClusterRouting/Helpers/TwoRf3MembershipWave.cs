using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TwoRf3MembershipWave : IAsyncDisposable
{
    internal readonly TwoRf3MembershipCapacity capacity = new();
    internal DistributedApplication? application;
    internal KeyLoad.IntegrationTests.Features.Messaging.RemoteTransferStartupCallbacks? startupCallbacks;
    internal ContainerRuntimeControl? RemoteRuntimeOwner { get; set; }
    internal string? dataRoot;
    private bool dataRootOwned;
    internal bool startAttempted;
    internal bool applicationDisposed;
    internal bool nodeLocksReleased;
    private bool cleanupFailed;
    private bool retainRoots;
    internal readonly bool registerPhysicalOwners;
    internal readonly bool remoteDocumentReads;
    internal readonly bool remotePartitionQueries;
    internal readonly bool queryProbe;
    internal readonly bool protectedDocuments;
    internal readonly bool activationIsolation;
    internal ReplicaIsolationOwner? IsolationOwner;
    internal string? remoteTransferPrincipalId;
    internal int? movementMaxBatchBytes;
    internal int? movementMaxFrameBytes;
    internal RequestCqrsProbeFixture? queryControls;
    internal MovementFrameObservationFixture? frameObservation;
    internal bool nativeDiscoveryOmissionSelected;
    internal KeyLoad.IntegrationTests.Features.StorageRecovery.NativeCapabilityOmissionRf3Fixture? nativeDiscoveryOmission;
    internal readonly LocalRf3ImageSelection.Selection? localImageSelection;
    internal LocalRf3ImageIdentity.Identity? localImageIdentity;

    internal TwoRf3MembershipWave(LocalRf3ImageSelection.Selection? selection, bool register = false, bool remote = false, bool query = false, bool probe = false, bool protectedDocument = false, int? maximumBatchBytes = null, bool isolateActivation = false)
    { localImageSelection = selection; registerPhysicalOwners = register; remoteDocumentReads = remote; remotePartitionQueries = query; queryProbe = probe; protectedDocuments = protectedDocument; movementMaxBatchBytes = maximumBatchBytes; activationIsolation = isolateActivation; }

    internal string OwnedDataRoot => dataRoot
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);

    internal string? MovementTargetPeerKey { get; set; }

    internal NodeEpochRf3Profile Profile
    {
        get => field
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); set;
    }
    internal ContainerRuntimeControl RemoteRuntime => RemoteRuntimeOwner
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
    internal DistributedApplication Application => application
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);

    internal static Task<TwoRf3MembershipWave> StartAsync(CancellationToken cancellationToken)
        => StartAsync(null, cancellationToken);

    internal static async Task<TwoRf3MembershipWave> StartAsync(LocalRf3ImageSelection.Selection? selection,
        CancellationToken cancellationToken)
    {
        return await StartOwnedAsync(new TwoRf3MembershipWave(selection), cancellationToken).ConfigureAwait(false);
    }

    internal static Task<TwoRf3MembershipWave> StartRegistrationAsync(CancellationToken cancellationToken)
        => StartOwnedAsync(new TwoRf3MembershipWave(null, register: true), cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartRemoteDocumentsAsync(CancellationToken cancellationToken)
        => StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true), cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartProtectedDocumentsAsync(CancellationToken token)
        => StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true, query: true, probe: true, protectedDocument: true), token);

    internal static Task<TwoRf3MembershipWave> StartNativeCapabilityOmissionAsync(CancellationToken token)
        => StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true, query: true,
            probe: true, protectedDocument: true)
        { nativeDiscoveryOmissionSelected = true }, token);

    internal static Task<TwoRf3MembershipWave> StartActivationIsolationAsync(CancellationToken token)
        => StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true, query: true,
            probe: true, protectedDocument: true, isolateActivation: true), token);

    internal static Task<TwoRf3MembershipWave> StartProtectedDocumentsAsync(int maxBatchBytes, CancellationToken token)
    {
        new DatabaseLimits { MaxBatchBytes = maxBatchBytes }.Validate();
        return StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true, query: true,
            probe: true, protectedDocument: true, maximumBatchBytes: maxBatchBytes), token);
    }

    internal static Task<TwoRf3MembershipWave> StartProtectedFramesAsync(int maxFrameBytes, CancellationToken token)
    {
        new KeyLoad.Storage.ZoneTree.ZoneTreeStorageExecutionOptions { MaxFrameBytes = maxFrameBytes }.Validate();
        var wave = new TwoRf3MembershipWave(null, register: true, remote: true, query: true,
            probe: true, protectedDocument: true)
        { movementMaxFrameBytes = maxFrameBytes };
        return StartOwnedAsync(wave, token);
    }

    internal static Task<TwoRf3MembershipWave> StartRemoteQueriesAsync(CancellationToken cancellationToken)
        => StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true, query: true), cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartProbedRemoteQueriesAsync(CancellationToken token)
        => StartOwnedAsync(new TwoRf3MembershipWave(null, register: true, remote: true, query: true, probe: true), token);

    internal RequestCqrsProbeFixture QueryControls => queryControls
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);

    internal static Task<TwoRf3MembershipWave> StartOwnedAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
        => TwoRf3MembershipWaveStartup.StartOwnedAsync(wave, cancellationToken);

    internal async Task WaitForSixHealthyAsync(CancellationToken cancellationToken)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await Application.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (queryControls is { } controls)
        { ServerFailureObserver.Observe(controls.StopAdmission, failures); }
        if (IsolationOwner is { } namespaceOwner)
        { await ServerFailureObserver.ObserveAsync(() => namespaceOwner.RestoreAsync(CancellationToken.None), failures); }
        await DisposeApplicationAsync(failures).ConfigureAwait(false);
        if (CanCheckLocks)
        { ServerFailureObserver.Observe(AssertAllNodeLocksReleased, failures); }
        if (applicationDisposed && nodeLocksReleased && queryControls is { } joinedControls)
        { await ServerFailureObserver.ObserveAsync(joinedControls.DisposeAfterResourcesJoinedAsync, failures).ConfigureAwait(false); }
        if (IsolationOwner is { } retiredNamespace)
        {
            await ServerFailureObserver.ObserveAsync(() => retiredNamespace.SettleAfterStopAsync(
            applicationDisposed && nodeLocksReleased, CancellationToken.None), failures);
        }
        if (applicationDisposed && nodeLocksReleased && frameObservation is { } joinedFrames)
        { ServerFailureObserver.Observe(() => joinedFrames.DeleteAfterOwnerJoin(this), failures); }
        if (applicationDisposed && nodeLocksReleased && nativeDiscoveryOmission is { } joinedDiscovery)
        { await ServerFailureObserver.ObserveAsync(() => joinedDiscovery.DisposeAfterOwnersJoinedAsync(this), failures).ConfigureAwait(false); }
        if (CanDeleteRoot(failures))
        {
            var root = dataRoot!;
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
            if (failures.Count == 0)
            { dataRoot = null; dataRootOwned = false; }
        }
        MovementTargetPeerKey = null;
        cleanupFailed |= failures.Count > 0;
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task<string> StopForDirectoryReadAsync()
    {
        var failures = new List<Exception>();
        await DisposeApplicationAsync(failures).ConfigureAwait(false);
        if (CanCheckLocks)
        { ServerFailureObserver.Observe(AssertAllNodeLocksReleased, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        if (!nodeLocksReleased)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        return dataRoot ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.InvalidRoot);
    }

    internal async Task DisposeApplicationAsync(List<Exception> failures)
    {
        var owned = application;
        if (owned is null)
        { return; }
        var before = failures.Count;
        using var deadline = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        if (failures.Count == before)
        { application = null; applicationDisposed = true; }
        else
        { cleanupFailed = true; }
        if (startupCallbacks is { } capture)
        { await ServerFailureObserver.ObserveAsync(capture.Join, failures).ConfigureAwait(false); }
    }

    internal bool CanCheckLocks => startAttempted && applicationDisposed && !nodeLocksReleased
        && !cleanupFailed && dataRoot is not null;

    internal void RetainRoots()
    {
        retainRoots = true;
        queryControls?.RetainEvidence();
    }

    private bool CanDeleteRoot(List<Exception> failures)
        => !retainRoots && dataRootOwned && dataRoot is not null && failures.Count == 0 && !cleanupFailed
            && (!startAttempted || nodeLocksReleased) && (application is null || applicationDisposed);

    internal void AssertAllNodeLocksReleased()
    {
        var root = dataRoot ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.InvalidRoot);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, "replica", "owner.lock"));
        }
        nodeLocksReleased = true;
    }

    internal string CreateOwnedRoot()
    {
        var path = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, "artifacts",
            "qualification", "membership-stage1a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        dataRoot = path;
        dataRootOwned = true;
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return path;
    }
    internal Task RestartJoinedAsync(CancellationToken cancellationToken)
        => capacity.RestartJoinedAsync(this, cancellationToken);

    internal Task ReconfigureMovementFrameAsync(int maxFrameBytes, CancellationToken cancellationToken)
        => capacity.ReconfigureMovementFrameAsync(this, maxFrameBytes, cancellationToken);

    internal Task ReconfigureMovementCapacityAsync(int maxBatchBytes, CancellationToken cancellationToken)
        => capacity.ReconfigureMovementCapacityAsync(this, maxBatchBytes, cancellationToken);

}
