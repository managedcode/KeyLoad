namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private readonly Lock remoteTransferPeerGate = new();
    private Features.Messaging.RemoteTransferPeerOwner? remoteTransferPeerOwner;

    internal void RegisterRemoteTransferPeerOwner(Features.Messaging.RemoteTransferPeerOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (remoteTransferPeerGate)
        {
            if (remoteTransferPeerOwner is not null)
            { throw Errors.Fail(ErrorCode.Conflict, Features.Messaging.RemoteTransferPeerProtocol.Unavailable); }
            remoteTransferPeerOwner = owner;
        }
    }

    internal void DetachRemoteTransferPeerOwner(Features.Messaging.RemoteTransferPeerOwner owner)
    {
        lock (remoteTransferPeerGate)
        {
            if (!ReferenceEquals(remoteTransferPeerOwner, owner))
            { throw Errors.Fail(ErrorCode.OwnershipLost, Features.Messaging.RemoteTransferPeerProtocol.Invalid); }
            owner.Close();
            remoteTransferPeerOwner = null;
        }
    }

    private Features.Messaging.RemoteTransferPeerOwner RequireRemoteTransferPeerOwner()
    {
        lock (remoteTransferPeerGate)
        {
            return remoteTransferPeerOwner ?? throw Errors.Fail(ErrorCode.UnsupportedCapability,
            Features.Messaging.RemoteTransferPeerProtocol.Unavailable);
        }
    }
}
