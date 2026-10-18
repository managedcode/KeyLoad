using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.Messaging;

internal sealed class RemoteTransferPeerVerificationOwner : IDisposable
{
    private readonly DatabaseEngine database;
    private readonly RemoteDocumentMac mac;
    private readonly RemoteTransferPeerOwner registration;
    private readonly Lock gate = new();
    private bool disposed;
    private NativeRequestWorkOwner? requests;
    private RemoteDocumentRuntime? remote;

    internal bool IsDisposed
    {
        get
        {
            lock (gate)
            {
                return disposed;
            }
        }
    }

    internal void AttachRequests(NativeRequestWorkOwner owner)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (requests is not null && !ReferenceEquals(requests, owner))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid);
            }
            requests = owner;
        }
    }

    internal void AttachRuntime(RemoteDocumentRuntime runtime)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (remote is not null && !ReferenceEquals(remote, runtime))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid);
            }
            remote = runtime;
        }
    }

    internal void RequireJoined()
    {
        lock (gate)
        {
            if (requests is { IsJoined: false } || remote is { IsJoined: false })
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Unavailable);
            }
        }
    }

    private RemoteTransferPeerVerificationOwner(DatabaseEngine database, RemoteDocumentMac mac,
        RemoteTransferPeerOwner registration)
    {
        this.database = database;
        this.mac = mac;
        this.registration = registration;
    }

    internal static RemoteTransferPeerVerificationOwner? Create(DatabaseEngine database,
        ServerRuntimeOptions options)
    {
        var node = options.Node.Value;
        if (node.RemoteTransferPrincipalId is null)
        {
            return null;
        }
        JsonData.Identifier(node.RemoteTransferPrincipalId);
        if (!node.PhysicalGroupTrust.RemoteDocumentReads
            || node.PhysicalGroupTrust.Role != global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupTrustRole.Destination)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferPeerProtocol.Unavailable);
        }
        var source = PhysicalOwnerConfiguredTuples.Control(node, options.ReplicaConfiguration.Value);
        var destination = PhysicalOwnerConfiguredTuples.Destination(node, options.ReplicaConfiguration.Value);
        var mac = RemoteDocumentMac.FromConfiguredSecret(node.PhysicalGroupTrust.ForeignPeerSecret);
        try
        {
            var registration = new RemoteTransferPeerOwner(source, destination, node.RemoteTransferPrincipalId,
                (bytes, signature) => Verify(mac, source, options.Membership, bytes, signature));
            database.RegisterRemoteTransferPeerOwner(registration);
            return new(database, mac, registration);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            ServerFailureObserver.Observe(mac.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static RemoteQueueTransferPeerCall Verify(RemoteDocumentMac mac, RegisteredPhysicalOwnerV1 source,
        global::Microsoft.Extensions.Options.IOptions<OrleansMembershipOptions> membership, ReadOnlyMemory<byte> bytes, string signature)
    {
        if (!mac.Verify(bytes.Span, signature, reply: false))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid);
        }
        var envelope = RemoteDocumentWire.DecodeCall(bytes.Span);
        var call = envelope.QueueTransfer;
        if (call is null || !RemoteTransferPeerNonce.Valid(call.Nonce) || envelope.Document is not null
            || !source.Owner.VoterIds.Contains(call.CallerVoter, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid);
        }
        if (!global::KeyLoad.Server.Features.ClusterRouting.PhysicalGroupAddress.CanonicalAddress(call.CallerSiloAddress, membership))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid);
        }
        return call;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            RequireJoined();
            database.DetachRemoteTransferPeerOwner(registration);
            mac.Dispose();
            disposed = true;
        }
    }
}
