using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.QueryExecution;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentRuntime : IAsyncDisposable
{
    private readonly RemoteDocumentWorkOwner work;
    private RemoteDocumentClient? client;

    private RemoteDocumentRuntime(ServerRuntimeOptions options)
        => work = new(options.GrainRouting, options.Node, options.RemoteDocuments);

    private readonly Lock gate = new();
    private Task? disposal;
    internal bool IsJoined => work.IsJoined;
    internal IRemoteDocumentReadRouter? Router { get; private set; }
    internal IRemoteBlobReadRouter? BlobRouter { get; private set; }
    internal IControlledDocumentCommandRouter? CommandRouter { get; private set; }
    internal IRemotePartitionQueryRouter? QueryRouter { get; private set; }
    internal RemoteDocumentEndpoint? Endpoint { get; private set; }

    internal static async Task<RemoteDocumentRuntime?> CreateAsync(OrleansNode node,
        PartitionHost partition, ServerRuntimeOptions options, TimeProvider clock,
        IControlledBlobWireBorrowObserver? wireObserver = null)
    {
        var settings = options.Node.Value.MembershipAuthority;
        if (!settings.RemoteDocumentReads)
        { return null; }
        var runtime = new RemoteDocumentRuntime(options);
        try
        {
            runtime.Initialize(node, partition, options, clock, wireObserver);
            return runtime;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { await runtime.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private void Initialize(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, TimeProvider clock, IControlledBlobWireBorrowObserver? wireObserver)
    {
        var settings = options.Node.Value.MembershipAuthority;
        if (settings.Mode == MembershipAuthoritySettingsProtocol.Authority)
        {
            client = new(options.Node, options.Membership, clock, wireObserver);
            Router = new RemoteDocumentRouter(node, partition, options.Node, options.Core.DatabaseLimits, client, work, clock);
            BlobRouter = new RemoteBlobReadRouter(node, partition, options.Node, options.Core.DatabaseLimits, client, work, clock);
            if (options.PartitionMovement.Value.Enabled)
            {
                CommandRouter = new ControlledDocumentCommandRouter(node, partition, options.Node,
                options.Core.DatabaseLimits,
                new ControlledBlobSourceRead(node, partition, options.Node, options.Core.DatabaseLimits, client, clock), clock);
            }
            if (settings.RemotePartitionQueries)
            { QueryRouter = new RemotePartitionQueryRouter(node, partition, options.Node, client, work, clock); }
        }
        else
        {
            var receiver = new RemoteDocumentReceiver(node, partition, options.Node,
                options.GrainRouting, options.Membership, clock);
            var controlled = new RemoteControlledDocumentReceiver(node, partition, options.Node,
                options.Membership, options.GrainRouting, clock);
            var controlledBlob = new RemoteControlledBlobReceiver(node, partition, options.Node,
                options.Membership, options.GrainRouting, clock);
            Endpoint = new(options.Node, receiver, work, controlled, controlledBlob, options.Membership, options.GrainRouting, clock);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        { disposal ??= DisposeCoreAsync(); return new ValueTask(disposal); }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(work.StopAsync, failures).ConfigureAwait(false);
        if (Endpoint is not null)
        {
            try
            { await Endpoint.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        if (client is not null)
        {
            try
            { client.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        try
        { await work.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
