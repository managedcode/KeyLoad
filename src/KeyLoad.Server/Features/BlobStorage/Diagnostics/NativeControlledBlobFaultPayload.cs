namespace KeyLoad.Server.Features.BlobStorage;

// Local diagnostic value: never a transport or persistence schema.
internal sealed record NativeControlledBlobFaultPayload(
    NativeControlledBlobFaultVariation Variation, ReadOnlyMemory<byte> Payload, string Signature);
