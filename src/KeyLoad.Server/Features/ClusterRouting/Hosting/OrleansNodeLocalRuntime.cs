using KeyLoad.Clustering;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server.Features.GraphTraversal;
using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.Server;

/// <summary>Acquires and retains one node's local native runtime, including partial initialization, through joined shutdown.</summary>
internal sealed class OrleansNodeLocalRuntime(IServiceProvider services, ServerRuntimeOptions options,
    TimeProvider clock) : IAsyncDisposable
{
    private const int NoCloseFailures = 0;
    private readonly TenantPlacementNodeOwner placement = new();
    private readonly Lock closing = new();
    private readonly List<Exception> closeFailures = [];
    private Task? shutdown;
    private RemoteDocumentRuntime? documents;
    private PartitionMovementRuntime? movement;
    private RemoteModelNativeRuntime? receiver;
    private RemoteModelNativeClient? client;
    private NativeBlobWireProbeObserver? blobObserver;
    private RuntimeJournalAdmission? journal;
    private ReplicaSiloDiscoveryState? discovery;
    private ReplicaEnvelopeAuthenticator? authentication;
    private ReplicaSiloDiscoveryClient? discoveryClient;
    private IAsyncDisposable? administration;
    private RequestCqrsProbeNativeRelay? relay;
    private RequestCqrsProbeObserver? observer;
    private ResourceOwnerInventoryNativeRuntime? resourceInventory;

    internal RemoteGraphNodeResources Graphs { get; } = new();
    internal RemoteDocumentRuntime? Documents => Volatile.Read(ref documents);
    internal PartitionMovementRuntime? Movement => Volatile.Read(ref movement);
    internal TenantPlacementNativeRuntime? TenantPlacement => placement.Runtime;
    internal ResourceOwnerInventoryNativeRuntime? ResourceInventory => Volatile.Read(ref resourceInventory);
    internal RuntimeJournalAdmission? Journal => Volatile.Read(ref journal);
    internal ReplicaSiloDiscoveryState? Discovery => Volatile.Read(ref discovery);
    internal ReplicaEnvelopeAuthenticator? Authentication => Volatile.Read(ref authentication);
    internal ReplicaSiloDiscoveryClient? DiscoveryClient => Volatile.Read(ref discoveryClient);

    internal async Task PrepareAsync(Task registered, OrleansNode node, PartitionHost partition,
        NativeRequestWorkOwner work, NodeOptions nodeOptions, PhysicalOwnerProbeWorkOwner? physicalOwnerWork,
        CancellationToken token)
    {
        await registered.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        work.BindPhysicalOwner(services.GetRequiredService<PhysicalShardRecord>());
        work.AttachResourceLifetimeWorkFence(partition.Database, partition.Materializer.Log.State.LastIndex);
        partition.TransferVerification?.AttachRequests(work);
        PhysicalOwnerRegistrationRuntime.RequireProbeWork(nodeOptions, physicalOwnerWork);
        Volatile.Write(ref documents, await RemoteDocumentRuntime.CreateAsync(node, partition,
            options, clock).ConfigureAwait(false));
        await Graphs.InitializeAsync(node, partition, options, clock).ConfigureAwait(false);
        Volatile.Write(ref movement, await PartitionMovementRuntime.CreateAsync(node, partition,
            options, work, clock).ConfigureAwait(false));
        await placement.InitializeAsync(node, partition, options, Movement, clock, token).ConfigureAwait(false);
        // Retain ownership before release publication; later initialization failures join DisposeCoreAsync.
        resourceInventory = await ResourceOwnerInventoryNativeFactory.CreateAsync(node, partition, options,
            services.GetRequiredService<ReplicaSiloDiscoveryState>(), clock).ConfigureAwait(false);
        Volatile.Write(ref resourceInventory, resourceInventory);
        Volatile.Write(ref discovery, services.GetRequiredService<ReplicaSiloDiscoveryState>());
        Volatile.Write(ref journal, services.GetRequiredService<RuntimeJournalAdmission>());
    }

    internal async Task CaptureAsync(Task preparation, Lock lifecycle, CancellationToken token)
    {
        await preparation.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            Volatile.Write(ref receiver, services.GetRequiredService<RemoteModelNativeRuntime>());
            Volatile.Write(ref blobObserver, services.GetService<NativeBlobWireProbeObserver>());
            Volatile.Write(ref client, services.GetRequiredService<RemoteModelNativeClient>());
            Volatile.Write(ref authentication, services.GetRequiredService<ReplicaEnvelopeAuthenticator>());
            Volatile.Write(ref discoveryClient, services.GetRequiredService<ReplicaSiloDiscoveryClient>());
            Volatile.Write(ref relay, services.GetService<RequestCqrsProbeNativeRelay>());
            Volatile.Write(ref observer, services.GetService<RequestCqrsProbeObserver>());
            Volatile.Write(ref administration, services.GetRequiredService<INodeAdministration>() as IAsyncDisposable);
            Graphs.Capture(services);
        }
    }

    internal void AttachNativeReceivers()
    {
        Documents?.AttachNativeReceiver(Volatile.Read(ref receiver)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable));
        Graphs.Attach();
    }

    internal bool IsDatabaseReady(NodeOptions nodeOptions, OrleansNodeDatabaseBootstrap bootstrap, bool catalogReady)
        => (nodeOptions.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Disabled
            || nodeOptions.PhysicalGroupTrust.RemoteDocumentReads
                && (nodeOptions.PhysicalGroupTrust.Role != PhysicalGroupTrustRole.Control
                    || bootstrap.OwnerRegistration?.Observe().Verified == true))
            && catalogReady && Journal?.IsReady == true;

    internal void StartPlacement() => placement.Start();

    internal Task JoinHostAsync(NativeRequestWorkOwner work, List<Exception> failures)
        => OrleansNodeHostShutdown.JoinAsync(work, Volatile.Read(ref administration),
            Volatile.Read(ref relay), Volatile.Read(ref observer), failures);

    internal async Task JoinResourcesAsync(OrleansNodeRequestLifetime requestLifetime,
        OrleansNodeDatabaseBootstrap bootstrap, PhysicalOwnerProbeWorkOwner? physicalOwnerWork,
        Action withdrawGrains, List<Exception> failures)
    {
        ServerFailureObserver.Observe(() => bootstrap.Catalog?.CloseAdmission(), failures);
        ServerFailureObserver.Observe(() => Journal?.CloseScheduling(), failures);
        if (ResourceInventory is { } inventory)
        {
            await ServerFailureObserver.ObserveAsync(inventory.CloseAdmissionAsync, failures).ConfigureAwait(false);
        }
        await JoinHostAsync(requestLifetime.Work, failures).ConfigureAwait(false);
        await JoinAsync(failures).ConfigureAwait(false);
        await PhysicalOwnerRegistrationRuntime.StopAndJoinAsync(bootstrap.OwnerRegistration,
            physicalOwnerWork, failures).ConfigureAwait(false);
        if (OrleansNodeClosure.KnownOwnersJoined(requestLifetime.Work, Documents, Movement, Graphs)
            && (ResourceInventory is null || ResourceInventory.IsJoined))
        {
            ServerFailureObserver.Observe(() => Journal?.Close(), failures);
            withdrawGrains();
            await requestLifetime.JoinAsync(failures).ConfigureAwait(false);
        }
        else
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RequestWorkNotJoined));
        }
    }

    internal async Task JoinAsync(List<Exception> failures)
    {
        var terminalFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), terminalFailures).ConfigureAwait(false);
        failures.AddRange(closeFailures.Count > NoCloseFailures ? closeFailures : terminalFailures);
    }

    /// <summary>Returns the original local-runtime shutdown; placement disposal follows the original movement join.</summary>
    public ValueTask DisposeAsync()
    {
        lock (closing)
        {
            if (shutdown is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                shutdown = DisposeCoreAsync(registered.Task);
                registered.SetResult();
            }
            return new(shutdown);
        }
    }

    private async Task DisposeCoreAsync(Task registered)
    {
        await registered.ConfigureAwait(false);
        if (ResourceInventory is { } inventory)
        {
            await ServerFailureObserver.ObserveAsync(() => inventory.DisposeAsync().AsTask(), closeFailures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(placement.CloseAdmissionAsync, closeFailures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => Graphs.JoinAsync(closeFailures), closeFailures).ConfigureAwait(false);
        if (Volatile.Read(ref receiver) is { } originalReceiver)
        {
            await ServerFailureObserver.ObserveAsync(originalReceiver.StopAsync, closeFailures).ConfigureAwait(false);
        }
        if (Volatile.Read(ref client) is { } originalClient)
        {
            await ServerFailureObserver.ObserveAsync(originalClient.StopAsync, closeFailures).ConfigureAwait(false);
        }
        if (Volatile.Read(ref blobObserver) is { } originalBlobObserver)
        {
            await ServerFailureObserver.ObserveAsync(originalBlobObserver.StopAsync, closeFailures).ConfigureAwait(false);
        }
        if (Movement is { } originalMovement)
        {
            await ServerFailureObserver.ObserveAsync(() => originalMovement.DisposeAsync().AsTask(), closeFailures).ConfigureAwait(false);
        }
        Task? placementClose = null;
        try
        {
            placementClose = placement.DisposeAsync().AsTask();
            await placementClose.ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            if (placementClose?.Exception is { } terminalFailure)
            {
                closeFailures.AddRange(terminalFailure.InnerExceptions);
            }
            else
            {
                closeFailures.Add(error);
            }
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            if (placementClose?.Exception is { } terminalFailure)
            {
                closeFailures.AddRange(terminalFailure.InnerExceptions);
            }
            else
            {
                closeFailures.Add(error);
            }
        }
        if (Documents is { } originalDocuments)
        {
            await ServerFailureObserver.ObserveAsync(() => originalDocuments.DisposeAsync().AsTask(), closeFailures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(closeFailures);
    }
}
