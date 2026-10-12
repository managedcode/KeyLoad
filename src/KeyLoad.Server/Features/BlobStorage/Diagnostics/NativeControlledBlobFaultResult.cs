using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeControlledBlobFaultResult(int Ordinal,
    NativeControlledBlobFaultVariation Variation, RemoteModelNativeRequest Request, RemoteModelNativeReply Reply);
