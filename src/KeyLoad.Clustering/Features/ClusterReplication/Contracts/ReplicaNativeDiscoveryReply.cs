namespace KeyLoad.Clustering;

/// <summary>Retains original metadata authentication and binds its complete result to the native request.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaNativeDiscoveryProtocol.ReplyAlias)]
public sealed record ReplicaNativeDiscoveryReply(
    [property: global::Orleans.Id(0)] ReadOnlyMemory<byte> Payload,
    [property: global::Orleans.Id(1)] string DiscoverySignature,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] ErrorCode? Error,
    [property: global::Orleans.Id(4)] string Signature);
