using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextOnlineCatalog.SerializationAlias)]
internal sealed record NativeTextOnlineCatalog(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] Guid NodeId,
    [property: global::Orleans.Id(2)] TextProjectionScope Scope,
    [property: global::Orleans.Id(3)] PhysicalShardRecord Placement,
    [property: global::Orleans.Id(4)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(5)] long ConsumerGeneration,
    [property: global::Orleans.Id(6)] string Leaf,
    [property: global::Orleans.Id(7)] long AppliedPosition,
    [property: global::Orleans.Id(8)] long ThroughSequence,
    [property: global::Orleans.Id(9)] string ManifestSha256,
    [property: global::Orleans.Id(10)] Guid BuildCommandId)
{
    internal const string SerializationAlias = "keyload.server.native-text.online-catalog.v1";
}
