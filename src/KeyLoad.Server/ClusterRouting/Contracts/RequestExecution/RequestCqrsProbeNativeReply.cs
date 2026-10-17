namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Bounded authenticated native producer-disposal reply.</summary>
/// <param name="Version">Frozen protocol version.</param>
/// <param name="Method">Original closed native method.</param>
/// <param name="Nonce">Original request nonce.</param>
/// <param name="Claim">Genuine producer claim when present.</param>
/// <param name="Acknowledged">Whether the original notification was acknowledged.</param>
/// <param name="Error">Original closed failure code when present.</param>
/// <param name="Signature">Exact native reply MAC.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RequestCqrsProbeNativeProtocol.ReplyAlias)]
internal sealed record RequestCqrsProbeNativeReply(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RequestCqrsProbeNativeMethod Method,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] RequestCqrsProbeNativeClaim? Claim,
    [property: global::Orleans.Id(4)] bool Acknowledged,
    [property: global::Orleans.Id(5)] ErrorCode? Error,
    [property: global::Orleans.Id(6)] string Signature);
