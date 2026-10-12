namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeBlobWireResult(int Version, string SessionId, string ClaimDigest,
    NativeBlobWireIdentity Identity, int Ordinal, NativeControlledBlobFaultVariation Variation,
    byte[] OriginalNativeRequest, byte[] NativeRequest, byte[] NativeReply);
