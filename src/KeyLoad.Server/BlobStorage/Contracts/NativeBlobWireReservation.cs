namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeBlobWireReservation(int Version, string SessionId, string OwnerDigest,
    string ClaimDigest, NativeBlobWireIdentity Identity, int Slot, Guid CallbackId);
