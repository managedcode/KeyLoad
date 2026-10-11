namespace KeyLoad.Server.Features.EventStreams;

// Ephemeral raw MAC evidence; current receiver discovery/pins/owner validation remains mandatory.
internal sealed record EventVectorMacCheckedReply(EventVectorRemoteReply Reply,
    ReadOnlyMemory<byte> OriginalReplyBytes, string OriginalSignature);
