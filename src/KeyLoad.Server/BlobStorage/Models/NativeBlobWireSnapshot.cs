namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeBlobWireSnapshot(NativeBlobWireOwner Owner, string OwnerDigest,
    NativeBlobWireArms? Arms, string? ArmsDigest);
