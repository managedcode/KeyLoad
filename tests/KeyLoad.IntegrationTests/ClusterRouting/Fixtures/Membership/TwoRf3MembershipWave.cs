using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TwoRf3MembershipWave : IAsyncDisposable
{
    internal readonly TwoRf3MembershipCapacity capacity = new();
    internal readonly TwoRf3MembershipBuilderLifetime builderLifetime = new();
    internal DistributedApplication? application;
    internal KeyLoad.IntegrationTests.Features.RelationalStorage.SqlTypedInsertMovementTls? SqlInsertTls
    {
        get; init;
    }
    internal KeyLoad.IntegrationTests.Features.Messaging.RemoteTransferStartupCallbacks? startupCallbacks;
    internal ContainerRuntimeControl? RemoteRuntimeOwner
    {
        get; set;
    }
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
    internal readonly bool nativeMembershipDiagnostics;
    internal Microsoft.Extensions.Options.IOptions<KeyLoad.Query.QueryExecutionOptions>? AnnReadExecution
    {
        get; init;
    }
    internal NativeMembershipDiagnosticFixture? nativeMembershipControls;
    internal bool nativeCanonicalCutsSelected;
    internal NativeCanonicalModelCutFixture? canonicalCutControls;
    internal readonly bool protectedDocuments;
    internal bool ProtectedMovementProbe
    {
        get; init;
    }
    internal bool TenantCapacityPlacement
    {
        get; init;
    }
    internal readonly bool activationIsolation;
    internal ReplicaIsolationOwner? IsolationOwner;
    internal string? remoteTransferPrincipalId;
    internal int? movementMaxBatchBytes;
    internal int? movementMaxFrameBytes;
    internal long? movementTargetReadBytes;
    internal RequestCqrsProbeFixture? queryControls;
    internal bool nativeBlobWireProbeSelected;
    internal NativeBlobWireProbeFixture? nativeBlobWireControls;
    internal MovementFrameObservationFixture? frameObservation;
    internal bool nativeDiscoveryOmissionSelected;
    internal bool EventVectorReservedProbe
    {
        get; set;
    }
    internal KeyLoad.IntegrationTests.Features.StorageRecovery.NativeCapabilityOmissionRf3Fixture? nativeDiscoveryOmission;
    internal readonly LocalRf3ImageSelection.Selection? localImageSelection;
    internal LocalRf3ImageIdentity.Identity? localImageIdentity;
    internal LocalRf3ImageTestSession? localImagePreparation;

    internal TwoRf3MembershipWave(LocalRf3ImageSelection.Selection? selection, bool register = false, bool remote = false, bool query = false, bool probe = false, bool protectedDocument = false, int? maximumBatchBytes = null, bool isolateActivation = false, bool nativeMembershipDiagnostics = false)
    {
        if (nativeMembershipDiagnostics && (remote || query || probe || protectedDocument
            || isolateActivation || maximumBatchBytes is not null))
        {
            throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        }
        localImageSelection = selection;
        registerPhysicalOwners = register;
        remoteDocumentReads = remote;
        remotePartitionQueries = query;
        queryProbe = probe;
        protectedDocuments = protectedDocument;
        movementMaxBatchBytes = maximumBatchBytes;
        activationIsolation = isolateActivation;
        this.nativeMembershipDiagnostics = nativeMembershipDiagnostics;
    }

    internal string OwnedDataRoot => dataRoot
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);

    internal string? MovementTargetPeerKey
    {
        get; set;
    }

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
        => TwoRf3MembershipWaveFactory.StartAsync(cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartAsync(LocalRf3ImageSelection.Selection? selection,
        CancellationToken cancellationToken)
        => TwoRf3MembershipWaveFactory.StartAsync(selection, cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartRegistrationAsync(CancellationToken cancellationToken)
        => TwoRf3MembershipWaveFactory.StartRegistrationAsync(cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartRemoteDocumentsAsync(CancellationToken cancellationToken)
        => TwoRf3MembershipWaveFactory.StartRemoteDocumentsAsync(cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartProtectedDocumentsAsync(CancellationToken token)
        => TwoRf3MembershipWaveFactory.StartProtectedDocumentsAsync(token);

    internal static Task<TwoRf3MembershipWave> StartProtectedDocumentsAsync(Action<string> observe, CancellationToken token)
        => TwoRf3MembershipWaveFactory.StartProtectedDocumentsAsync(observe, token);

    internal static Task<TwoRf3MembershipWave> StartNativeCapabilityOmissionAsync(CancellationToken token, LocalRf3ImageSelection.Selection? selection = null,
        Action<string>? observe = null, KeyLoad.IntegrationTests.Features.Messaging.RemoteTransferStartupCallbacks? callbacks = null)
        => TwoRf3MembershipWaveFactory.StartNativeCapabilityOmissionAsync(token, selection, observe, callbacks);

    internal static Task<TwoRf3MembershipWave> StartActivationIsolationAsync(CancellationToken token)
        => TwoRf3MembershipWaveFactory.StartActivationIsolationAsync(token);

    internal static Task<TwoRf3MembershipWave> StartProtectedDocumentsAsync(int maxBatchBytes, CancellationToken token)
        => TwoRf3MembershipWaveFactory.StartProtectedDocumentsAsync(maxBatchBytes, token);

    internal static Task<TwoRf3MembershipWave> StartProtectedFramesAsync(int maxFrameBytes, CancellationToken token)
        => TwoRf3MembershipWaveFactory.StartProtectedFramesAsync(maxFrameBytes, token);

    internal static Task<TwoRf3MembershipWave> StartRemoteQueriesAsync(CancellationToken cancellationToken)
        => TwoRf3MembershipWaveFactory.StartRemoteQueriesAsync(cancellationToken);

    internal static Task<TwoRf3MembershipWave> StartProbedRemoteQueriesAsync(CancellationToken token)
        => TwoRf3MembershipWaveFactory.StartProbedRemoteQueriesAsync(token);

    internal RequestCqrsProbeFixture QueryControls => queryControls
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);

    internal static Task<TwoRf3MembershipWave> StartOwnedAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
        => TwoRf3MembershipWaveStartup.StartOwnedAsync(wave, cancellationToken);

    internal Task WaitForSixHealthyAsync(CancellationToken cancellationToken)
        => TwoRf3MembershipWaveStartup.WaitForSixHealthyAsync(this, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await TwoRf3MembershipOwnerJoin.JoinAsync(this, failures).ConfigureAwait(false);
        if (CanDeleteRoot(failures))
        {
            var root = dataRoot!;
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
            if (failures.Count == 0)
            {
                dataRoot = null;
                dataRootOwned = false;
            }
        }
        MovementTargetPeerKey = null;
        cleanupFailed |= failures.Count > 0;
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal Task<string> StopForDirectoryReadAsync()
        => NativeMembershipDiagnosticWaveLifecycle.StopForDirectoryReadAsync(this);

    internal async Task DisposeApplicationAsync(List<Exception> failures)
    {
        NativeCanonicalModelCutWaveLifecycle.StopAdmission(this, failures);
        await builderLifetime.DisposeBeforeTransferAsync(this, failures).ConfigureAwait(false);
        var owned = application;
        if (owned is null)
        {
            return;
        }
        if (await TwoRf3ApplicationShutdown.JoinAsync(owned, failures).ConfigureAwait(false))
        {
            application = null;
            applicationDisposed = true;
        }
        else
        {
            cleanupFailed = true;
        }
        if (startupCallbacks is { } capture)
        {
            await ServerFailureObserver.ObserveAsync(capture.Join, failures).ConfigureAwait(false);
        }
    }

    internal bool CanCheckLocks => startAttempted && applicationDisposed && !nodeLocksReleased
        && !cleanupFailed && dataRoot is not null;

    internal void RetainRoots()
    {
        retainRoots = true;
        TwoRf3MembershipBuilderLifetime.RetainControls(this);
    }

    private bool CanDeleteRoot(List<Exception> failures)
        => !retainRoots && !builderLifetime.RetentionVeto && dataRootOwned && dataRoot is not null && failures.Count == 0 && !cleanupFailed
            && (!startAttempted || nodeLocksReleased) && (application is null || applicationDisposed);

    internal void AssertAllNodeLocksReleased()
        => TwoRf3MembershipNodeLockJoin.AssertAll(this);

    internal string CreateOwnedRoot()
    {
        var path = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, "artifacts",
            "qualification", "membership-stage1a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        dataRoot = path;
        dataRootOwned = true;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }
    internal Task RestartJoinedAsync(CancellationToken cancellationToken)
        => capacity.RestartJoinedAsync(this, cancellationToken);

    internal Task ReconfigureMovementFrameAsync(int maxFrameBytes, CancellationToken cancellationToken)
        => capacity.ReconfigureMovementFrameAsync(this, maxFrameBytes, cancellationToken);

    internal Task ReconfigureMovementCapacityAsync(int maxBatchBytes, CancellationToken cancellationToken)
        => capacity.ReconfigureMovementCapacityAsync(this, maxBatchBytes, cancellationToken);

}
