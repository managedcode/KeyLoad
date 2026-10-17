namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeBlobWireResultEntry(int Ordinal,
    NativeControlledBlobFaultVariation Variation, string RecordDigest);
