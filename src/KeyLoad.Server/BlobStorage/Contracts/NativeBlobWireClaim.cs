namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeBlobWireClaim(int Version, string SessionId, string OwnerDigest,
    string ArmsDigest, Guid ArmId, NativeBlobWireIdentity Identity, string CallerVoter,
    string CallerSiloAddress, Guid RequestId, Guid QueryId, string Nonce,
    DateTimeOffset ExpiresAt, string OriginalCallDigest);
