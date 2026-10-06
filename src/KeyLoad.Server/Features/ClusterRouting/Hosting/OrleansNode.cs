using System.Net;
using System.Net.Sockets;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns one native silo lifetime and routes public operations to uniquely keyed request actors.</summary>
/// <param name="partition">Borrowed physical database and replica owner.</param>
/// <param name="nodeOptions">Validated fixed-voter silo settings.</param>
/// <param name="administration">Borrowed node administration guarded by read actors.</param>
/// <param name="loggerFactory">Shared process diagnostics, owned by the outer application.</param>
/// <param name="membershipAuthority">Borrowed discovery authority, drained before the native silo stops.</param>
/// <param name="runtimeOptions">Shared validated options snapshots borrowed by the native silo.</param>
internal sealed class OrleansNode(PartitionHost partition, IOptions<NodeOptions> nodeOptions,
    INodeAdministration administration, ILoggerFactory loggerFactory, ReplicaMembershipAuthorityOwner membershipAuthority,
    ServerRuntimeOptions runtimeOptions) : IAsyncDisposable
{
    private NodeOptions Options => nodeOptions.Value;
    private readonly Lock lifecycle = new();
    private readonly NativeRequestWorkOwner requestWork = new(runtimeOptions.GrainRouting);
    private readonly OrleansNodeRequestExecutor requests = new(runtimeOptions.GrainRouting,
        loggerFactory.CreateLogger<OrleansNode>());
    private IHost? host;
    private IGrainFactory? grains;
    private Task? shutdown;
    private Task? startup;
    private PhysicalShardCatalogStartup? physicalShardCatalog;
    private int siloJoined;

    /// <summary>Factory published only after native silo startup completes.</summary>
    public IGrainFactory? Grains => Volatile.Read(ref grains);

    /// <summary>Allows physical cleanup only after every admitted native frame has actually joined.</summary>
    internal bool HasJoinedRequestWork => requestWork.IsJoined;

    /// <summary>Early runtime discovery remains available during membership bootstrap.</summary>
    public ReplicaSiloDiscoveryState? Discovery => Volatile.Read(ref host)?.Services.GetRequiredService<ReplicaSiloDiscoveryState>();

    internal bool CatalogReady => Grains is not null && Volatile.Read(ref physicalShardCatalog)?.IsReady == true;

    internal bool DatabaseReady => Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Local
        && CatalogReady && Volatile.Read(ref host)?.Services.GetRequiredService<RuntimeJournalAdmission>().IsReady == true;

    internal bool SiloJoined => Volatile.Read(ref siloJoined) == OrleansNodeProtocol.JoinedSilo;

    internal Task<MembershipReadinessSnapshot?> MembershipReadyAsync(CancellationToken cancellationToken)
        => ReplicaMembershipReadiness.ReadNodeAsync(SiloJoined, Volatile.Read(ref host), Options, cancellationToken);

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
    public GrainRequestIdentityScope OpenRequestContext(PrincipalRecord? principal, Guid requestId, Guid commandId,
        CancellationToken cancellationToken)
        => new(RuntimeServices, principal, requestId, commandId, cancellationToken);

    private IServiceProvider RuntimeServices => Grains is not null && Volatile.Read(ref host) is { } running
        ? running.Services : throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);

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
        var address = await ResolveAddressAsync(cancellationToken).ConfigureAwait(false);
        var built = OrleansSiloConfiguration.Build(partition, Options, administration, loggerFactory, requestWork,
            address, cancellationToken, runtimeOptions);
        Volatile.Write(ref host, built);
        await built.StartAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref siloJoined, OrleansNodeProtocol.JoinedSilo);
        Volatile.Write(ref grains, built.Services.GetRequiredService<IGrainFactory>());
        if (Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Proxy)
        { return; }
        var catalog = new PhysicalShardCatalogStartup(this, partition, nodeOptions,
            built.Services.GetRequiredService<TimeProvider>(), runtimeOptions.GrainRouting);
        Volatile.Write(ref physicalShardCatalog, catalog);
        var administrator = await catalog.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Local)
        {
            await RuntimeJournalStartup.InitializeAsync(built.Services, partition, administrator, Options,
                runtimeOptions, cancellationToken).ConfigureAwait(false);
        }
        if (Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Authority)
        { membershipAuthority.Publish(built.Services.GetRequiredService<IMembershipTable>()); }
    }

    private async Task<IPAddress> ResolveAddressAsync(CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(Options.SiloAddress, out var literal) ? [literal]
            : await Dns.GetHostAddressesAsync(Options.SiloAddress, cancellationToken).ConfigureAwait(false);
        var allowLoopback = Options.Peers.All(peer => new Uri(peer).IsLoopback);
        return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
            && !address.Equals(IPAddress.Any) && (allowLoopback || !IPAddress.IsLoopback(address)))
            ?? throw new InvalidOperationException(OrleansNodeProtocol.InvalidSiloAddress);
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
    {
        if (requireCatalogAdmission && !DatabaseReady)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady); }
        var factory = Grains ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        var catalog = requireCatalogAdmission ? Volatile.Read(ref physicalShardCatalog) : null;
        if (requireCatalogAdmission && (catalog is null || !catalog.IsReady))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady); }
        return requests.ExecuteAsync(factory, RuntimeServices, catalog, requestId, signedRequest, command,
            cancellationToken);
    }

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

    private async Task StopCoreAsync(Task registered, Task? starting)
    {
        await registered.ConfigureAwait(false);
        if (starting is not null)
        { await starting.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
        Volatile.Read(ref physicalShardCatalog)?.CloseAdmission();
        var failures = new List<Exception>();
        var stopping = Volatile.Read(ref host);
        if (Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Authority)
        {
            await membershipAuthority.StopAdmissionJoinAndClearProviderAsync(failures).ConfigureAwait(false);
        }
        if (stopping is not null)
        {
            await StopHostAsync(stopping, failures).ConfigureAwait(false);
        }
        else
        {
            await ServerFailureObserver.ObserveAsync(requestWork.DrainAsync, failures).ConfigureAwait(false);
        }
        try
        {
            await requestWork.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task StopHostAsync(IHost stopping, List<Exception> failures)
    {
        var shutdownTimeout = runtimeOptions.Membership.Value.ShutdownTimeout;
        using var deadline = new CancellationTokenSource(shutdownTimeout);
        await ServerFailureObserver.ObserveAsync(() => stopping.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(requestWork.DrainAsync, failures).ConfigureAwait(false);
        if (!requestWork.IsJoined)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RequestWorkNotJoined);
        }
        stopping.Services.GetRequiredService<RuntimeJournalAdmission>().Close();
        Volatile.Write(ref grains, null);
        // A failed native Start has no lifecycle rollback. Drain the endpoint while its transport still exists.
        ServerFailureObserver.Observe(() => stopping.Services.GetService<ReplicaSiloDiscoveryState>()?.StopDiscovery(), failures);
        await ServerFailureObserver.ObserveAsync(() => partition.Consensus.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        Volatile.Write(ref host, null);
        if (stopping is IAsyncDisposable asynchronous)
        { await ServerFailureObserver.ObserveAsync(() => asynchronous.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        else
        { ServerFailureObserver.Observe(stopping.Dispose, failures); }
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
    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));
}
