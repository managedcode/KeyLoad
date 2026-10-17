namespace KeyLoad.Server.Features.EventStreams;

// Ephemeral raw MAC evidence; configured pins/replay/current owner admission still precedes payload decode.
internal sealed record EventVectorMacCheckedCall(EventVectorRemoteCall Call,
    ReadOnlyMemory<byte> OriginalRequestBytes, string OriginalSignature);
