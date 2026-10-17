namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Bounded signed native producer-disposal request.</summary>
/// <param name="Version">Frozen protocol version.</param>
/// <param name="Method">Closed native method.</param>
/// <param name="Identity">Original authenticated request and connection identity.</param>
/// <param name="CallerOwner">Configured caller physical owner.</param>
/// <param name="CallerVoter">Configured caller voter name.</param>
/// <param name="CallerSiloAddress">Full caller native address including generation.</param>
/// <param name="DestinationOwner">Configured destination physical owner.</param>
/// <param name="DestinationVoter">Configured destination voter name.</param>
/// <param name="DestinationSiloAddress">Full destination native address including generation.</param>
/// <param name="Nonce">Original one-shot request nonce.</param>
/// <param name="ExpiresAt">Original native request expiry.</param>
/// <param name="Claim">Original producer claim for notifications.</param>
/// <param name="Signature">Exact native request MAC.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RequestCqrsProbeNativeProtocol.RequestAlias)]
internal sealed record RequestCqrsProbeNativeRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RequestCqrsProbeNativeMethod Method,
    [property: global::Orleans.Id(2)] RequestCqrsProbeNativeIdentity Identity,
    [property: global::Orleans.Id(3)] PhysicalShardRecord CallerOwner,
    [property: global::Orleans.Id(4)] string CallerVoter,
    [property: global::Orleans.Id(5)] string CallerSiloAddress,
    [property: global::Orleans.Id(6)] PhysicalShardRecord DestinationOwner,
    [property: global::Orleans.Id(7)] string DestinationVoter,
    [property: global::Orleans.Id(8)] string DestinationSiloAddress,
    [property: global::Orleans.Id(9)] Guid Nonce,
    [property: global::Orleans.Id(10)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(11)] RequestCqrsProbeNativeClaim? Claim,
    [property: global::Orleans.Id(12)] string Signature);
