using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Borrows the application silo and coordinates its original node-local database work.</summary>
/// <param name="applicationServices">The same application provider hosting the native silo and published storage.</param>
/// <param name="nodeOptions">Validated fixed-voter settings.</param>
/// <param name="loggerFactory">Borrowed process diagnostics.</param>
/// <param name="runtimeOptions">The shared validated options snapshots.</param>
/// <param name="clock">Borrowed runtime clock.</param>
/// <param name="physicalOwnerWork">Borrowed physical-owner probe work joined before native teardown.</param>
/// <param name="discoveryOmission">Optional native discovery observation.</param>
/// <param name="partitionMovementNativeObserver">Optional original native movement observation.</param>
internal sealed partial class OrleansNode(IServiceProvider applicationServices, IOptions<NodeOptions> nodeOptions,
    ILoggerFactory loggerFactory, ServerRuntimeOptions runtimeOptions, TimeProvider? clock = null,
    PhysicalOwnerProbeWorkOwner? physicalOwnerWork = null,
    NativeDiscoveryOmissionOwner? discoveryOmission = null,
    IPartitionMovementNativeObserver? partitionMovementNativeObserver = null) : IAsyncDisposable
{
    internal NativeDiscoveryOmissionOwner? DiscoveryOmission => discoveryOmission;
    internal IPartitionMovementNativeObserver? NativeMovementObservation => partitionMovementNativeObserver;
    private readonly Lock lifecycle = new();
    private readonly OrleansNodeRequestLifetime requestLifetime = new(runtimeOptions.GrainRouting);
    private readonly OrleansNodeLocalRuntime localRuntime = new(applicationServices, runtimeOptions,
        clock ?? TimeProvider.System);
    private readonly OrleansNodeRequestExecutor requests = new(runtimeOptions.GrainRouting,
        loggerFactory.CreateLogger<OrleansNode>());
    private readonly NativeConnectionOwnerIdentity connectionOwner = new();
    private PartitionHost? physicalPartition;
    private PartitionHost partition => Volatile.Read(ref physicalPartition)
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
    private Task? localInitialization;
    private Task? localPreparation;
    internal NativeRequestWorkOwner RequestWork => requestLifetime.Work;
    internal NativeConnectionOwnerIdentity ConnectionOwner => connectionOwner;
    internal TenantPlacementNativeRuntime? TenantPlacementRuntime => localRuntime.TenantPlacement;
    internal global::KeyLoad.Server.Features.ResourceExecution.ResourceOwnerInventoryNativeRuntime? ResourceInventoryRuntime => localRuntime.ResourceInventory;
    private IGrainFactory? grains;
    private Task? shutdown;
    private readonly OrleansNodeClosure closure = new();
    internal Task? Closing => Volatile.Read(ref shutdown);
    private Task? startup;
    private readonly OrleansNodeStartupWait startupWait = new();
    private readonly OrleansNodeDatabaseBootstrap databaseBootstrap = new();
    internal PartitionMovementRuntime? Movement => localRuntime.Movement;
    internal RemoteDocumentRuntime? RemoteDocuments => localRuntime.Documents;
    private int siloJoined;
    internal global::KeyLoad.Server.Features.GraphTraversal.RemoteGraphNodeResources GraphResources => localRuntime.Graphs;

    /// <summary>Factory published only after native silo startup completes.</summary>
    public IGrainFactory? Grains => Volatile.Read(ref grains);

    /// <summary>Allows physical cleanup only after every admitted native frame has actually joined.</summary>
    internal bool HasJoinedRequestWork => closure.IsJoined;

    /// <summary>Early runtime discovery remains available during membership bootstrap.</summary>
    public ReplicaSiloDiscoveryState? Discovery => localRuntime.Discovery;

    internal Task<KeyLoad.Clustering.ReplicaCohortAdmissionStatus> AcquireCohortAdmissionAsync(CancellationToken token)
        => RuntimeServices.GetRequiredService<ReplicaSiloDiscoveryClient>().AcquireCompatibleCohortAsync(token);

    internal bool CatalogReady => Grains is not null && databaseBootstrap.Catalog?.IsReady == true;

    internal bool DatabaseReady => localRuntime.IsDatabaseReady(nodeOptions.Value, databaseBootstrap, CatalogReady);

    internal PhysicalOwnerRegistrationObservation? OwnerRegistration => databaseBootstrap.OwnerRegistration?.Observe();

    internal Task WaitForPhysicalOwnerRegistrationAsync(CancellationToken cancellationToken)
        => PhysicalOwnerRegistrationRuntime.WaitForRegistrationAsync(databaseBootstrap.OwnerRegistration, nodeOptions.Value, cancellationToken);

    internal bool SiloJoined => Volatile.Read(ref siloJoined) == OrleansNodeProtocol.JoinedSilo;

    internal Task<MembershipReadinessSnapshot?> MembershipReadyAsync(CancellationToken cancellationToken)
        => ReplicaMembershipReadiness.ReadNodeAsync(SiloJoined, applicationServices, nodeOptions,
            runtimeOptions.Membership, cancellationToken);

    /// <summary>Authenticator for exact discovery response bytes.</summary>
    public ReplicaEnvelopeAuthenticator? Authentication => localRuntime.Authentication;

    /// <summary>True only while authenticated compatible fixed-voter observations establish a fresh cohort.</summary>
    public bool HasCompatibleCohort => Grains is not null
        && localRuntime.DiscoveryClient?.HasCompatibleCohort == true;

    public Guid InternalConnectionId => connectionOwner.Id;

    internal IServiceProvider RuntimeServices => Grains is not null
        ? applicationServices : throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);

    internal Task InitializeLocalRuntimeAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (shutdown is not null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
            }
            if (localInitialization is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Volatile.Write(ref physicalPartition, applicationServices.GetRequiredService<PartitionHostApplicationOwner>().Borrow());
                var initializationToken = requestLifetime.CreateLocal(cancellationToken,
                    applicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                localPreparation = localRuntime.PrepareAsync(registered.Task, this, partition, RequestWork,
                    nodeOptions.Value, physicalOwnerWork, initializationToken);
                localInitialization = localRuntime.CaptureAsync(localPreparation, lifecycle, initializationToken);
                registered.SetResult();
            }
            return localInitialization.WaitAsync(cancellationToken);
        }
    }

    internal void RequireLocalRuntimePublished()
    {
        if (Volatile.Read(ref localPreparation)?.IsCompletedSuccessfully != true)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        }
    }

    internal void PublishNativeRuntime(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shutdown is not null || localInitialization?.IsCompletedSuccessfully != true)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
            }
            if (Grains is not null)
            {
                return;
            }
            localRuntime.AttachNativeReceivers();
            Volatile.Write(ref siloJoined, OrleansNodeProtocol.JoinedSilo);
            Volatile.Write(ref grains, applicationServices.GetRequiredService<IGrainFactory>());
            localRuntime.StartPlacement();
        }
    }

    /// <summary>Bootstraps persisted database authority after the application silo has become Active.</summary>
    /// <param name="cancellationToken">Cancellation for the original post-active database bootstrap.</param>
    /// <returns>The original database bootstrap task, without starting another host.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (startup is not null || shutdown is not null || Grains is null)
            {
                throw new InvalidOperationException(OrleansNodeProtocol.SiloAlreadyStarted);
            }
            var bootstrapToken = requestLifetime.CreateBootstrap(cancellationToken,
                applicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
            var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            startup = databaseBootstrap.StartAsync(registered.Task, this, partition, nodeOptions,
                runtimeOptions, applicationServices, clock ?? TimeProvider.System, loggerFactory, bootstrapToken);
            startupWait.Publish(startup);
            registered.SetResult();
            return startup;
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
        => requests.ExecuteAsync(CurrentRequestTarget, true, requestId, signedRequest, command, cancellationToken);

    internal Task<GrainOperationReply> ExecutePhysicalShardStartupRequestAsync(Guid requestId,
        string signedRequest, bool command, CancellationToken cancellationToken)
        => requests.ExecuteAsync(CurrentRequestTarget, false, requestId, signedRequest, command, cancellationToken);

    private NativeNodeRequestTarget CurrentRequestTarget => new(Grains, applicationServices,
        databaseBootstrap.Catalog, DatabaseReady);

    /// <summary>Joins native request work before stopping membership, replica lifecycles or physical owners.</summary>
    /// <param name="cancellationToken">Cancellation bounds this caller's wait, without interrupting owned cleanup.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (shutdown is null)
            {
                startupWait.Close();
                databaseBootstrap.Catalog?.CloseAdmission();
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                shutdown = StopCoreAsync(registered.Task, localInitialization, startup);
                registered.SetResult();
            }
            return shutdown.WaitAsync(cancellationToken);
        }
    }

    internal Task EnsureCatalogAdmissionAsync(CancellationToken cancellationToken)
        => PhysicalShardCatalogFence.EnsureAdmissionAsync(databaseBootstrap.Catalog, cancellationToken);

    internal Task EnsureCatalogAdmissionAsync(ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant? sourceMeta, CancellationToken token)
        => PhysicalShardCatalogFence.EnsureAdmissionAsync(databaseBootstrap.Catalog, work, sourceMeta, token);

    internal void OpenCatalogAdmission(PhysicalShardCatalogStartup catalog,
        CancellationToken cancellationToken)
        => databaseBootstrap.OpenAdmission(lifecycle, ref shutdown, catalog, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        if (closure.IsJoined)
        {
            await ServerFailureObserver.ObserveAsync(() => localRuntime.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => requestLifetime.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
