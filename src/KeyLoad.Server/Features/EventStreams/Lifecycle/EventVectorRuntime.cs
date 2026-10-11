using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorRuntime : IDisposable
{
    private RemoteDocumentMac? localMac;
    private RemoteDocumentMac? callerMac;
    private ReplicaMembershipAuthorityAddressPins? pins;
    private ReplicaMembershipAuthorityReplayCache? replay;
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
        var settings = options.Node.Value.MembershipAuthority;
        var authority = new EventVectorRemoteCallValidation(partition, options.Node,
            options.GrainRouting, options.Membership, clock);
        var encoding = new EventVectorRemoteEncoding(options.Core.DatabaseLimits);
        var native = new EventVectorNativeReceivingCall(node, partition, authority, clock);
        var receiver = new EventVectorRemoteReceiver(node, partition, authority, native, encoding);
        localMac = RemoteDocumentMac.FromConfiguredSecret(options.Node.Value.PeerSecret);
        replay = new(clock, options.Membership);
        if (settings.Mode == MembershipAuthoritySettingsProtocol.Authority)
        {
            Local = new(node, receiver, authority, encoding, localMac, replay);
        }
        else
        {
            callerMac = RemoteDocumentMac.FromConfiguredSecret(settings.AuthorityPeerSecret);
            pins = EventVectorReceiverPins.Create(authority, options.Membership);
            Endpoint = new(receiver, authority, encoding, callerMac, localMac, pins, replay,
                originalWork, options.GrainRouting, clock);
        }
    }

    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        try
        {
            try
            { replay?.Dispose(); }
            catch (Exception primary)
            {
                var failures = new List<Exception> { primary };
                if (pins is not null)
                { ServerFailureObserver.Observe(pins.Dispose, failures); }
                if (callerMac is not null)
                { ServerFailureObserver.Observe(callerMac.Dispose, failures); }
                if (localMac is not null)
                { ServerFailureObserver.Observe(localMac.Dispose, failures); }
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
            try
            { pins?.Dispose(); }
            catch (Exception primary)
            {
                var failures = new List<Exception> { primary };
                if (callerMac is not null)
                { ServerFailureObserver.Observe(callerMac.Dispose, failures); }
                if (localMac is not null)
                { ServerFailureObserver.Observe(localMac.Dispose, failures); }
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
            try
            { callerMac?.Dispose(); }
            catch (Exception primary)
            {
                var failures = new List<Exception> { primary };
                if (localMac is not null)
                { ServerFailureObserver.Observe(localMac.Dispose, failures); }
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
            localMac?.Dispose();
        }
        finally
        {
            Local = null;
            Endpoint = null;
            replay = null;
            pins = null;
            callerMac = null;
            localMac = null;
        }
    }
}
