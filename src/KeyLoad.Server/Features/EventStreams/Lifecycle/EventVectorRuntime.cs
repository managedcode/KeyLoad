using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorRuntime : IDisposable
{
    private RemoteDocumentMac? localMac;
    private RemoteDocumentMac? callerMac;
    private EventVectorReplayCache? replay;
    private bool disposed;

    internal EventVectorLocalExchange? Local { get; private set; }
    internal EventVectorEndpointExecution? Endpoint { get; private set; }

    internal static EventVectorRuntime Create(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, RemoteDocumentWorkOwner originalWork, TimeProvider clock)
    {
        var runtime = new EventVectorRuntime();
        try
        {
            runtime.Initialize(node, partition, options, originalWork, clock);
            return runtime;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(runtime.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private void Initialize(OrleansNode node, PartitionHost partition, ServerRuntimeOptions options,
        RemoteDocumentWorkOwner originalWork, TimeProvider clock)
    {
        var settings = options.Node.Value.PhysicalGroupTrust;
        if (!settings.RemoteDocumentReads || settings.Role is not (global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupTrustRole.Control or global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupTrustRole.Destination))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, EventVectorRemoteProtocol.Unavailable); }
        var authority = new EventVectorRemoteCallValidation(partition, options.Node,
            options.GrainRouting, options.Membership, clock);
        var encoding = new EventVectorRemoteEncoding(options.Core.DatabaseLimits);
        var native = new EventVectorNativeReceivingCall(node, partition, authority, clock);
        var receiver = new EventVectorRemoteReceiver(node, partition, authority, native, encoding);
        localMac = RemoteDocumentMac.FromConfiguredSecret(options.Node.Value.PeerSecret);
        replay = new(clock, options.Membership);
        if (settings.Role == global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupTrustRole.Control)
        { Local = new(node, receiver, authority, encoding, localMac, replay); }
        else
        {
            callerMac = RemoteDocumentMac.FromConfiguredSecret(settings.ForeignPeerSecret);
            Endpoint = new(receiver, authority, encoding, callerMac, localMac, replay,
                originalWork, options.GrainRouting, clock);
        }
    }

    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        var failures = new List<Exception>();
        if (replay is not null)
        { DisposeReplay(failures); }
        if (callerMac is not null)
        { DisposeCallerMac(failures); }
        if (localMac is not null)
        { DisposeLocalMac(failures); }
        Local = null;
        Endpoint = null;
        replay = null;
        callerMac = null;
        localMac = null;
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void DisposeReplay(List<Exception> failures)
    {
        try
        { replay!.Dispose(); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    private void DisposeCallerMac(List<Exception> failures)
    {
        try
        { callerMac!.Dispose(); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    private void DisposeLocalMac(List<Exception> failures)
    {
        try
        { localMac!.Dispose(); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }
}
