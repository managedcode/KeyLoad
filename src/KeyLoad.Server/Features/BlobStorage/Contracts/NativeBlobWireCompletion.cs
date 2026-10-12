namespace KeyLoad.Server.Features.BlobStorage;

/// <summary>Catalog-only evidence; does not attest original dispatch or owner shutdown.</summary>
internal sealed record NativeBlobWireCompletion(int Version, string SessionId, string ClaimDigest,
    NativeBlobWireIdentity Identity, int Count, NativeBlobWireResultEntry[] Results);
