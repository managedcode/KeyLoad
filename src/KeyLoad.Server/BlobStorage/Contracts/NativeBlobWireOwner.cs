namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeBlobWireOwner(int Version, string SessionId, string ClusterId,
    NativeBlobWireOwnerTuple ControlOwner, NativeBlobWireOwnerTuple SourceOwner,
    NativeBlobWireOwnerTuple DestinationOwner);
