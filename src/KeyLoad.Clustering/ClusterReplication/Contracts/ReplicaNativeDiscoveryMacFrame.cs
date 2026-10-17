namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaNativeDiscoveryProtocol.MacFrameAlias)]
internal sealed record ReplicaNativeDiscoveryMacFrame(
    [property: global::Orleans.Id(0)] string Purpose,
    [property: global::Orleans.Id(1)] ReadOnlyMemory<byte> Digest);
