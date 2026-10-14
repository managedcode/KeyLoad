using KeyLoad.Clustering;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.Messaging;
using KeyLoad.Server.Features.QueryExecution;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentRuntime : IAsyncDisposable
{
    private readonly RemoteDocumentWorkOwner work;
    private RemoteDocumentClient? client;
    private RemoteModelNativeRuntime? nativeReceiver;

    internal RemoteDocumentRuntime(ServerRuntimeOptions options)
        => work = new(options.GrainRouting, options.Node, options.RemoteDocuments);

    private readonly Lock gate = new();
    private Task? disposal;
    internal bool IsJoined => work.IsJoined;
    internal IRemoteDocumentReadRouter? Router
    {
        get; private set;
    }
    internal IRemoteBlobReadRouter? BlobRouter
    {
        get; private set;
    }
    internal IControlledDocumentCommandRouter? CommandRouter
    {
        get; private set;
    }
    internal IRemotePartitionQueryRouter? QueryRouter
    {
        get; private set;
    }
    internal IRemoteDistributedSearchRouter? DistributedSearchRouter
    {
        get; private set;
    }
    internal RemoteDocumentEndpoint? Endpoint
    {
        get; private set;
    }
    internal global::KeyLoad.Server.Features.EventStreams.EventVectorRuntime? EventVectors
    {
        get; private set;
    }
    internal IEventVectorParent? EventVectorParent
    {
        get; private set;
    }
    internal global::KeyLoad.Server.Features.Search.AnnProjectionNativeReceiver? AnnObservations
    {
        get; private set;
    }
    internal IRemoteAnnReadRouter? AnnReadRouter
    {
        get; private set;
    }

    internal static Task<RemoteDocumentRuntime?> CreateAsync(OrleansNode node,
        PartitionHost partition, ServerRuntimeOptions options, TimeProvider clock,
        IControlledBlobWireBorrowObserver? wireObserver = null)
        => RemoteDocumentRuntimeCreation.CreateAsync(node, partition, options, clock, wireObserver);

    internal void Initialize(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, TimeProvider clock, IControlledBlobWireBorrowObserver? wireObserver)
    {
        var settings = options.Node.Value.PhysicalGroupTrust;
        if (settings.Role == PhysicalGroupTrustRole.Control)
        {
            client = new(options.Node, options.Membership, clock, node.BorrowRemoteModelClient, wireObserver);
            Router = new RemoteDocumentRouter(node, partition, options.Node, options.Core.DatabaseLimits, client, work, clock);
            BlobRouter = new RemoteBlobReadRouter(node, partition, options.Node, options.Core.DatabaseLimits, client, work, clock);
            if (options.PartitionMovement.Value.Enabled)
            {
                CommandRouter = new ControlledDocumentCommandRouter(node, partition, options.Node,
                options.Core.DatabaseLimits,
                new ControlledBlobSourceRead(node, partition, options.Node, options.Core.DatabaseLimits, client, clock), clock);
            }
            CommandRouter = new RemoteTransferCommandRouter(node, partition, options.Node,
                options.Core.DatabaseLimits, client, work, clock, CommandRouter);
            if (settings.RemotePartitionQueries)
            {
                QueryRouter = new RemotePartitionQueryRouter(node, partition, options.Node, client, work, clock);
                DistributedSearchRouter = new RemoteDistributedSearchRouter(node, partition, options.Node,
                    options.Core.QueryExecution, client, work, clock);
            }
        }
        else
        {
            var receiver = new RemoteDocumentReceiver(node, partition, options.Node,
                options.GrainRouting, options.Membership, clock);
            var controlled = new RemoteControlledDocumentReceiver(node, partition, options.Node,
                options.Membership, options.GrainRouting, clock);
            var controlledBlob = new RemoteControlledBlobReceiver(node, partition, options.Node,
                options.Membership, options.GrainRouting, clock);
            var transfer = partition.TransferVerification is null ? null
                : new RemoteTransferPeerReceiver(node, partition, options.GrainRouting, clock);
            Endpoint = new(options.Node, receiver, work, controlled, controlledBlob,
                options.Membership, options.GrainRouting, clock, transfer);
        }
        EventVectors = global::KeyLoad.Server.Features.EventStreams.EventVectorRuntime.Create(node, partition, options, work, clock);
        AnnObservations = new(node, partition, options, clock);
        AnnReadRouter = new global::KeyLoad.Server.Features.Search.RemoteAnnReadRouter(node, partition,
            options.Node, options.Core.DatabaseLimits, options.Core.QueryExecution, work, clock);
        if (settings.Role == PhysicalGroupTrustRole.Control)
        {
            EventVectorParent = new global::KeyLoad.Server.Features.EventStreams.EventVectorFirstAdmitParent(node,
            partition, this, options.Node, options.Core.DatabaseLimits, options.GrainRouting, clock);
        }
    }

    internal void AttachNativeReceiver(RemoteModelNativeRuntime receiver)
        => RemoteDocumentRuntimeLifecycle.Attach(this, ref nativeReceiver, receiver);

    internal Task<global::KeyLoad.Server.Features.EventStreams.EventVectorAuthenticatedExchange> SendLocalEventVectorAsync(
        global::KeyLoad.Server.Features.EventStreams.EventVectorRemoteCall originalCall, KeyLoad.Core.ReadExecutionBudget originalWork,
        CancellationToken originalCancellationToken)
        => global::KeyLoad.Server.Features.EventStreams.EventVectorLocalCall.ExecuteAsync(work, EventVectors?.Local,
            originalCall, originalWork, originalCancellationToken);

    internal Task<global::KeyLoad.Server.Features.EventStreams.EventVectorAuthenticatedExchange> SendRemoteEventVectorAsync(
        RegisteredPhysicalOwnerV1 destination, global::KeyLoad.Server.Features.EventStreams.EventVectorRemoteCall originalCall,
        global::KeyLoad.Server.Features.EventStreams.EventVectorRemoteEncoding encoding, KeyLoad.Core.ReadExecutionBudget originalWork,
        CancellationToken originalCancellationToken)
        => global::KeyLoad.Server.Features.EventStreams.EventVectorBorrowedRemoteCall.ExecuteAsync(work, client,
            destination, originalCall, encoding, originalWork, originalCancellationToken);

    internal static bool MayHaveNativeEffects(RemoteModelNativeChannel channel, ReadOnlyMemory<byte> original)
        => RemoteModelNativeEffects.MayHaveEffects(channel, original);

    internal Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteNativeAsync(
        RemoteModelNativeRequest request, CancellationToken token)
        => ExecuteNativeAsync(request, null, token);

    internal Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteNativeAsync(
        RemoteModelNativeRequest request, RemoteModelNativeEffectBoundary? effectBoundary, CancellationToken token)
        => RemoteModelNativeDispatch.ExecuteAsync(this, request, effectBoundary, token);

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposal ??= DisposeCoreAsync();
            return new ValueTask(disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        if (nativeReceiver is not null)
        {
            await ServerFailureObserver.ObserveAsync(nativeReceiver.StopAsync, failures).ConfigureAwait(false);
        }
        if (client is not null)
        {
            await DisposeClientAsync(failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(work.StopAsync, failures).ConfigureAwait(false);
        EventVectorParent = null;
        if (EventVectors is not null)
        {
            ServerFailureObserver.Observe(EventVectors.Dispose, failures);
        }
        if (Endpoint is not null)
        {
            try
            {
                await Endpoint.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        try
        {
            await work.DisposeAsync().ConfigureAwait(false);
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
    private async Task DisposeClientAsync(List<Exception> failures)
    {
        Task terminal;
        try
        {
            terminal = DisposeClientCoreAsync();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
            return;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
            return;
        }
        await RemoteDocumentRuntimeLifecycle.SettleClientAsync(terminal, failures).ConfigureAwait(false);
    }

    private async Task DisposeClientCoreAsync()
        => await client!.DisposeAsync().ConfigureAwait(false);
}
