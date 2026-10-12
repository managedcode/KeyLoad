using KeyLoad.Orleans;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns one native silo lifetime and routes public operations to uniquely keyed request actors.</summary>
/// <param name="partition">Borrowed physical database and replica owner.</param>
/// <param name="nodeOptions">Validated fixed-voter silo settings.</param>
/// <param name="administration">Borrowed node administration guarded by read actors.</param>
/// <param name="loggerFactory">Shared process diagnostics, owned by the outer application.</param>
/// <param name="runtimeOptions">Shared validated options snapshots borrowed by the native silo.</param>
/// <param name="clock">Borrowed runtime clock for the silo and its shutdown deadlines.</param>
/// <param name="physicalOwnerWork">Borrowed configured proof work owner, joined before native silo shutdown.</param>
/// <param name="sealedObserver">Optional explicitly borrowed in-process observation, never execution authority.</param>
/// <param name="blobWireObserver">Optional fixture-owned signed-byte borrow, joined with the original request.</param>
/// <param name="discoveryOmission">Optional fixture observer of actual native discovery replies.</param>
/// <param name="physicalOwnerProbe">Borrowed probe runtime factory resolved after the outer node exists.</param>
/// <param name="localhostClustering">Explicit fixture-only native endpoint tuple; production uses Aspire Azure membership.</param>
/// <param name="partitionMovementNativeObserver">Borrowed fixture-owned peer observation, joined with the actual native call.</param>
internal sealed partial class OrleansNode(PartitionHost partition, IOptions<NodeOptions> nodeOptions,
    INodeAdministration administration, ILoggerFactory loggerFactory,
    ServerRuntimeOptions runtimeOptions, TimeProvider? clock = null, PhysicalOwnerProbeWorkOwner? physicalOwnerWork = null,
    IGrainPartitionMovementSealedOperationObserver? sealedObserver = null,
    IControlledBlobWireBorrowObserver? blobWireObserver = null,
    NativeDiscoveryOmissionOwner? discoveryOmission = null,
    Func<PhysicalOwnerProbeNativeRuntime>? physicalOwnerProbe = null,
    NativeLocalhostClusteringOptions? localhostClustering = null,
    IPartitionMovementNativeObserver? partitionMovementNativeObserver = null) : IAsyncDisposable
{
    internal NativeDiscoveryOmissionOwner? DiscoveryOmission => discoveryOmission;
    internal IPartitionMovementNativeObserver? NativeMovementObservation => partitionMovementNativeObserver;
    private NodeOptions Options => nodeOptions.Value;
    private readonly TimeProvider runtimeClock = clock ?? TimeProvider.System;
    private readonly Lock lifecycle = new();
    private readonly NativeRequestWorkOwner requestWork = new(runtimeOptions.GrainRouting);
    private readonly OrleansNodeRequestExecutor requests = new(runtimeOptions.GrainRouting,
        loggerFactory.CreateLogger<OrleansNode>());
    private readonly NativeConnectionOwnerIdentity connectionOwner = new();
    private IHost? host;
    private IGrainFactory? grains;
    private Task? shutdown;
    private Task? startup;
    private PhysicalShardCatalogStartup? physicalShardCatalog;
    private PhysicalOwnerRegistrationRuntime? ownerRegistration;
    private PartitionMovementRuntime? movement;
    internal PartitionMovementRuntime? Movement => Volatile.Read(ref movement);
    private RemoteDocumentRuntime? remoteDocuments;
    private RemoteModelNativeRuntime? nativeModelReceiver;
    private RemoteModelNativeClient? nativeModelClient;
    private global::KeyLoad.Server.Features.BlobStorage.NativeBlobWireProbeObserver? nativeBlobWireObserver;
    internal RemoteDocumentRuntime? RemoteDocuments => Volatile.Read(ref remoteDocuments);
    private int siloJoined;
    internal global::KeyLoad.Server.Features.GraphTraversal.RemoteGraphNodeResources GraphResources { get; } = new();

    /// <summary>Factory published only after native silo startup completes.</summary>
    public IGrainFactory? Grains => Volatile.Read(ref grains);

    /// <summary>Allows physical cleanup only after every admitted native frame has actually joined.</summary>
    internal bool HasJoinedRequestWork => requestWork.IsJoined && (RemoteDocuments?.IsJoined ?? true)
        && (Movement?.IsJoined ?? true) && GraphResources.IsJoined;

    /// <summary>Early runtime discovery remains available during membership bootstrap.</summary>
    public ReplicaSiloDiscoveryState? Discovery => Volatile.Read(ref host)?.Services.GetRequiredService<ReplicaSiloDiscoveryState>();

    internal Task<KeyLoad.Orleans.ReplicaCohortAdmissionStatus> AcquireCohortAdmissionAsync(CancellationToken token)
        => RuntimeServices.GetRequiredService<ReplicaSiloDiscoveryClient>().AcquireCompatibleCohortAsync(token);

    internal bool CatalogReady => Grains is not null && Volatile.Read(ref physicalShardCatalog)?.IsReady == true;

    internal bool DatabaseReady => (Options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Disabled
        || Options.PhysicalGroupTrust.RemoteDocumentReads
            && (Options.PhysicalGroupTrust.Role != PhysicalGroupTrustRole.Control || OwnerRegistration?.Verified == true))
        && CatalogReady && Volatile.Read(ref host)?.Services.GetRequiredService<RuntimeJournalAdmission>().IsReady == true;

    internal PhysicalOwnerRegistrationObservation? OwnerRegistration => Volatile.Read(ref ownerRegistration)?.Observe();

    internal Task WaitForPhysicalOwnerRegistrationAsync(CancellationToken cancellationToken)
        => PhysicalOwnerRegistrationRuntime.WaitForRegistrationAsync(Volatile.Read(ref ownerRegistration), Options, cancellationToken);

    internal bool SiloJoined => Volatile.Read(ref siloJoined) == OrleansNodeProtocol.JoinedSilo;

    internal Task<MembershipReadinessSnapshot?> MembershipReadyAsync(CancellationToken cancellationToken)
        => ReplicaMembershipReadiness.ReadNodeAsync(SiloJoined, Volatile.Read(ref host), nodeOptions,
            runtimeOptions.Membership, cancellationToken);

    /// <summary>Authenticator for exact discovery response bytes.</summary>
    public ReplicaEnvelopeAuthenticator? Authentication => Volatile.Read(ref host)?.Services.GetRequiredService<ReplicaEnvelopeAuthenticator>();

    /// <summary>True only while authenticated compatible fixed-voter observations establish a fresh cohort.</summary>
    public bool HasCompatibleCohort => Grains is not null
        && Volatile.Read(ref host)?.Services.GetRequiredService<ReplicaSiloDiscoveryClient>().HasCompatibleCohort == true;

    public Guid InternalConnectionId => connectionOwner.Id;

    internal IServiceProvider RuntimeServices => Grains is not null && Volatile.Read(ref host) is { } running
        ? running.Services : throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);

    /// <summary>Starts the native transport before its quorum-backed membership provider initializes.</summary>
    /// <param name="cancellationToken">Application startup cancellation, shared with membership initialization.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (startup is not null || shutdown is not null)
            { throw new InvalidOperationException(OrleansNodeProtocol.SiloAlreadyStarted); }
            return startup = Features.Messaging.RemoteTransferNativeStartup.Start(
                partition.TransferVerification, requestWork, StartCoreAsync, cancellationToken);
        }
    }

    private async Task StartCoreAsync(Task registered, CancellationToken cancellationToken)
    {
        await registered.ConfigureAwait(false);
        PhysicalOwnerRegistrationRuntime.RequireProbeWork(Options, physicalOwnerWork);
        var address = await OrleansNodeAddressResolver.ResolveAsync(Options, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref remoteDocuments, await RemoteDocumentRuntime.CreateAsync(this, partition,
            runtimeOptions, runtimeClock, blobWireObserver).ConfigureAwait(false));
        await GraphResources.InitializeAsync(this, partition, runtimeOptions, runtimeClock).ConfigureAwait(false);
        Volatile.Write(ref movement, await PartitionMovementRuntime.CreateAsync(this, partition,
            runtimeOptions, requestWork, runtimeClock).ConfigureAwait(false));
        var built = OrleansSiloConfiguration.Build(partition, Options, administration, loggerFactory, requestWork, connectionOwner,
            address, runtimeOptions, runtimeClock, Movement?.Source, Movement, RemoteDocuments?.Router, RemoteDocuments?.BlobRouter, RemoteDocuments?.QueryRouter, RemoteDocuments?.DistributedSearchRouter, RemoteDocuments?.CommandRouter, sealedObserver, discoveryOmission, RemoteDocuments?.EventVectorParent, physicalOwnerProbe?.Invoke(), localhostClustering, RemoteDocuments?.AnnReadRouter, GraphResources.Runtime, cancellationToken);
        Volatile.Write(ref host, built);
        // Capture the actual singleton owners before native startup; failed startup joins these same instances.
        Volatile.Write(ref nativeModelReceiver, built.Services.GetRequiredService<RemoteModelNativeRuntime>());
        Volatile.Write(ref nativeBlobWireObserver, built.Services.GetService<global::KeyLoad.Server.Features.BlobStorage.NativeBlobWireProbeObserver>());
        Volatile.Write(ref nativeModelClient, built.Services.GetRequiredService<RemoteModelNativeClient>());
        GraphResources.Capture(built.Services);
        await built.StartAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref siloJoined, OrleansNodeProtocol.JoinedSilo);
        Volatile.Write(ref grains, built.Services.GetRequiredService<IGrainFactory>());
        RemoteDocuments?.AttachNativeReceiver(Volatile.Read(ref nativeModelReceiver)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable));
        GraphResources.Attach();
        if (Options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Destination
            && !Options.PhysicalGroupTrust.RemoteDocumentReads)
        { return; }
        var catalog = new PhysicalShardCatalogStartup(this, partition, nodeOptions,
            built.Services.GetRequiredService<TimeProvider>(), runtimeOptions.GrainRouting, runtimeOptions.McpExecution,
            loggerFactory.CreateLogger<PhysicalShardCatalogStartup>());
        Volatile.Write(ref physicalShardCatalog, catalog);
        var administrator = await catalog.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await RuntimeJournalStartup.InitializeConfiguredAsync(built.Services, partition, administrator, Options,
            runtimeOptions, cancellationToken).ConfigureAwait(false);
        PhysicalOwnerRegistrationRuntime.Attach(ref ownerRegistration, this, partition,
            runtimeOptions, built.Services, runtimeClock);
    }

    /// <summary>Executes one signed operation through the corresponding GUID request actor.</summary>
    /// <param name="requestId">Fresh public request ID, distinct from the stable command ID.</param>
    /// <param name="signedRequest">Bounded trusted envelope issued by the shared request codec.</param>
    /// <param name="command">Server-derived command intent used only to classify an uncertain RPC outcome.</param>
    /// <param name="cancellationToken">Public request cancellation propagated through Orleans.</param>
    /// <returns>Exact bounded reply, throwing only the typed public safe error locally.</returns>
    public Task<GrainOperationReply> ExecuteAsync(Guid requestId, string signedRequest, bool command,
        CancellationToken cancellationToken)
        => requests.ExecuteAsync(CurrentRequestTarget, true, requestId, signedRequest, command, cancellationToken);

    internal Task<GrainOperationReply> ExecutePhysicalShardStartupRequestAsync(Guid requestId,
        string signedRequest, bool command, CancellationToken cancellationToken)
        => requests.ExecuteAsync(CurrentRequestTarget, false, requestId, signedRequest, command, cancellationToken);

    private NativeNodeRequestTarget CurrentRequestTarget => new(Grains, Volatile.Read(ref host)?.Services,
        Volatile.Read(ref physicalShardCatalog), DatabaseReady);

    /// <summary>Joins native request work before stopping membership, replica lifecycles or physical owners.</summary>
    /// <param name="cancellationToken">Cancellation bounds this caller's wait, without interrupting owned cleanup.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (shutdown is null)
            {
                Volatile.Read(ref physicalShardCatalog)?.CloseAdmission();
                Volatile.Read(ref host)?.Services.GetRequiredService<RuntimeJournalAdmission>().CloseScheduling();
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                shutdown = StopCoreAsync(registered.Task, startup);
                registered.SetResult();
            }
            return shutdown.WaitAsync(cancellationToken);
        }
    }

    internal Task EnsureCatalogAdmissionAsync(CancellationToken cancellationToken)
        => PhysicalShardCatalogFence.EnsureAdmissionAsync(Volatile.Read(ref physicalShardCatalog), cancellationToken);

    internal void OpenCatalogAdmission(PhysicalShardCatalogStartup catalog, string principalId,
        CancellationToken cancellationToken)
        => OrleansNodeCatalogAdmission.Open(lifecycle, ref shutdown, ref physicalShardCatalog,
            catalog, principalId, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await requestWork.DisposeAsync().ConfigureAwait(false);
    }
}
