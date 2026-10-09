using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server.Features.BlobStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns one native silo lifetime and routes public operations to uniquely keyed request actors.</summary>
/// <param name="partition">Borrowed physical database and replica owner.</param>
/// <param name="nodeOptions">Validated fixed-voter silo settings.</param>
/// <param name="administration">Borrowed node administration guarded by read actors.</param>
/// <param name="loggerFactory">Shared process diagnostics, owned by the outer application.</param>
/// <param name="membershipAuthority">Borrowed discovery authority, drained before the native silo stops.</param>
/// <param name="runtimeOptions">Shared validated options snapshots borrowed by the native silo.</param>
/// <param name="clock">Borrowed runtime clock for the silo and its shutdown deadlines.</param>
/// <param name="physicalOwnerWork">Borrowed configured proof work owner, joined before native silo shutdown.</param>
/// <param name="sealedObserver">Optional explicitly borrowed in-process observation, never execution authority.</param>
/// <param name="blobWireObserver">Optional fixture-owned signed-byte borrow, joined with the original request.</param>
internal sealed partial class OrleansNode(PartitionHost partition, IOptions<NodeOptions> nodeOptions,
    INodeAdministration administration, ILoggerFactory loggerFactory, ReplicaMembershipAuthorityOwner membershipAuthority,
    ServerRuntimeOptions runtimeOptions, TimeProvider? clock = null, PhysicalOwnerProbeWorkOwner? physicalOwnerWork = null,
    IGrainPartitionMovementSealedOperationObserver? sealedObserver = null,
    IControlledBlobWireBorrowObserver? blobWireObserver = null) : IAsyncDisposable
{
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
    internal RemoteDocumentRuntime? RemoteDocuments => Volatile.Read(ref remoteDocuments);
    private int siloJoined;

    /// <summary>Factory published only after native silo startup completes.</summary>
    public IGrainFactory? Grains => Volatile.Read(ref grains);

    /// <summary>Allows physical cleanup only after every admitted native frame has actually joined.</summary>
    internal bool HasJoinedRequestWork => requestWork.IsJoined && (RemoteDocuments?.IsJoined ?? true) && (Movement?.IsJoined ?? true);

    /// <summary>Early runtime discovery remains available during membership bootstrap.</summary>
    public ReplicaSiloDiscoveryState? Discovery => Volatile.Read(ref host)?.Services.GetRequiredService<ReplicaSiloDiscoveryState>();

    internal bool CatalogReady => Grains is not null && Volatile.Read(ref physicalShardCatalog)?.IsReady == true;

    internal bool DatabaseReady => (Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Local
        || Options.MembershipAuthority.RemoteDocumentReads
            && (Options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority || OwnerRegistration?.Verified == true))
        && CatalogReady && Volatile.Read(ref host)?.Services.GetRequiredService<RuntimeJournalAdmission>().IsReady == true;

    internal PhysicalOwnerRegistrationObservation? OwnerRegistration => Volatile.Read(ref ownerRegistration)?.Observe();

    internal bool SiloJoined => Volatile.Read(ref siloJoined) == OrleansNodeProtocol.JoinedSilo;

    internal Task<MembershipReadinessSnapshot?> MembershipReadyAsync(CancellationToken cancellationToken)
        => ReplicaMembershipReadiness.ReadNodeAsync(SiloJoined, Volatile.Read(ref host), nodeOptions,
            runtimeOptions.Membership, cancellationToken);

    /// <summary>Authenticator for exact discovery response bytes.</summary>
    public ReplicaEnvelopeAuthenticator? Authentication => Volatile.Read(ref host)?.Services.GetRequiredService<ReplicaEnvelopeAuthenticator>();

    /// <summary>True only while authenticated compatible fixed-voter observations establish a fresh cohort.</summary>
    public bool HasCompatibleCohort => Grains is not null
        && Volatile.Read(ref host)?.Services.GetRequiredService<ReplicaSiloDiscoveryClient>().HasCompatibleCohort == true;

    /// <summary>Checks all configured voters within the caller's deadline before routing or readiness.</summary>
    /// <param name="cancellationToken">Bounds the actual discovery attempts and their cleanup.</param>
    public Task EnsureCompatibleCohortAsync(CancellationToken cancellationToken)
        => RuntimeServices.GetRequiredService<ReplicaSiloDiscoveryClient>().EnsureCompatibleCohortAsync(cancellationToken);

    /// <summary>Admits and scopes persisted identity with this running silo's native serializers.</summary>
    /// <param name="principal">The persisted subject, absent only for credential authentication.</param>
    /// <param name="requestId">The fresh request identity.</param>
    /// <param name="commandId">The stable command identity, empty for reads.</param>
    /// <param name="cancellationToken">Cancels validation and actual native encoding.</param>
    /// <param name="connectionId">Actual accepted transport owner, or the registered service owner when absent.</param>
    public GrainRequestIdentityScope OpenRequestContext(PrincipalRecord? principal, Guid requestId, Guid commandId,
        CancellationToken cancellationToken, Guid? connectionId = null)
        => new(RuntimeServices, principal, requestId, commandId, cancellationToken,
            connectionId: connectionId ?? NativeConnectionExecutionIdentity.Resolve(RuntimeServices));

    public Guid InternalConnectionId => connectionOwner.Id;

    private IServiceProvider RuntimeServices => Grains is not null && Volatile.Read(ref host) is { } running
        ? running.Services : throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);

    internal KeyLoad.Query.QueryEngine BorrowPartitionQueries() => RuntimeServices.GetRequiredService<KeyLoad.Query.QueryEngine>();

    internal GrainRequestCodec CatalogRequestCodec() => RuntimeServices.GetRequiredService<GrainRequestCodec>();

    /// <summary>Starts the native transport before its quorum-backed membership provider initializes.</summary>
    /// <param name="cancellationToken">Application startup cancellation, shared with membership initialization.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (startup is not null || shutdown is not null)
            { throw new InvalidOperationException(OrleansNodeProtocol.SiloAlreadyStarted); }
            var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            startup = StartCoreAsync(registered.Task, cancellationToken);
            registered.SetResult();
            return startup;
        }
    }

    private async Task StartCoreAsync(Task registered, CancellationToken cancellationToken)
    {
        await registered.ConfigureAwait(false);
        if (Options.MembershipAuthority.RegisterPhysicalOwners && physicalOwnerWork is null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
        var address = await OrleansNodeAddressResolver.ResolveAsync(Options, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref remoteDocuments, await RemoteDocumentRuntime.CreateAsync(this, partition,
            runtimeOptions, runtimeClock, blobWireObserver).ConfigureAwait(false));
        Volatile.Write(ref movement, await PartitionMovementRuntime.CreateAsync(this, partition,
            runtimeOptions, requestWork, runtimeClock).ConfigureAwait(false));
        var built = OrleansSiloConfiguration.Build(partition, Options, administration, loggerFactory, requestWork, connectionOwner,
            address, runtimeOptions, runtimeClock, Movement?.Source, Movement, RemoteDocuments?.Router, RemoteDocuments?.BlobRouter, RemoteDocuments?.QueryRouter, RemoteDocuments?.CommandRouter, sealedObserver, cancellationToken);
        Volatile.Write(ref host, built);
        await built.StartAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref siloJoined, OrleansNodeProtocol.JoinedSilo);
        Volatile.Write(ref grains, built.Services.GetRequiredService<IGrainFactory>());
        if (Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Proxy
            && !Options.MembershipAuthority.RemoteDocumentReads)
        { return; }
        var catalog = new PhysicalShardCatalogStartup(this, partition, nodeOptions,
            built.Services.GetRequiredService<TimeProvider>(), runtimeOptions.GrainRouting, runtimeOptions.McpExecution,
            loggerFactory.CreateLogger<PhysicalShardCatalogStartup>());
        Volatile.Write(ref physicalShardCatalog, catalog);
        var administrator = await catalog.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Local
            || Options.MembershipAuthority.RemoteDocumentReads)
        {
            await RuntimeJournalStartup.InitializeAsync(built.Services, partition, administrator, Options,
                runtimeOptions, cancellationToken).ConfigureAwait(false);
        }
        if (Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Authority)
        {
            membershipAuthority.Publish(built.Services.GetRequiredService<IMembershipTable>());
            PhysicalOwnerRegistrationRuntime.Attach(ref ownerRegistration, this, partition, runtimeOptions,
                built.Services, runtimeClock);
        }
    }

    /// <summary>Executes one signed operation through the corresponding GUID request actor.</summary>
    /// <param name="requestId">Fresh public request ID, distinct from the stable command ID.</param>
    /// <param name="signedRequest">Bounded trusted envelope issued by the shared request codec.</param>
    /// <param name="command">Server-derived command intent used only to classify an uncertain RPC outcome.</param>
    /// <param name="cancellationToken">Public request cancellation propagated through Orleans.</param>
    /// <returns>Exact bounded reply, throwing only the typed public safe error locally.</returns>
    public Task<GrainOperationReply> ExecuteAsync(Guid requestId, string signedRequest, bool command,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(requestId, signedRequest, command, requireCatalogAdmission: true, cancellationToken);

    internal Task<GrainOperationReply> ExecutePhysicalShardStartupRequestAsync(Guid requestId,
        string signedRequest, bool command, CancellationToken cancellationToken)
        => ExecuteCoreAsync(requestId, signedRequest, command, requireCatalogAdmission: false, cancellationToken);

    private Task<GrainOperationReply> ExecuteCoreAsync(Guid requestId, string signedRequest, bool command,
        bool requireCatalogAdmission, CancellationToken cancellationToken)
        => requests.ExecuteAsync(Grains, Volatile.Read(ref host)?.Services,
            Volatile.Read(ref physicalShardCatalog), DatabaseReady, requireCatalogAdmission,
            requestId, signedRequest, command, cancellationToken);

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
    {
        var catalog = Volatile.Read(ref physicalShardCatalog);
        if (catalog is null || !catalog.IsReady)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady); }
        return catalog.EnsureAdmissionAsync(cancellationToken);
    }

    internal void OpenCatalogAdmission(PhysicalShardCatalogStartup catalog, string principalId,
        CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shutdown is not null || !ReferenceEquals(Volatile.Read(ref physicalShardCatalog), catalog))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady); }
            catalog.OpenAdmission(principalId);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await requestWork.DisposeAsync().ConfigureAwait(false);
    }
}
