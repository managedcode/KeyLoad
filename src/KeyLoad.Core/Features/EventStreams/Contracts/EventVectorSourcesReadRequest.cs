using System.Collections.Immutable;

namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorSourcesReadRequest.SerializerAlias)]
internal sealed record EventVectorSourcesReadRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] ImmutableArray<ReadEventSourceRequest> Requests,
    [property: Orleans.Id(2)] PhysicalShardRecord SourceOwner)
{
    internal const string SerializerAlias = "keyload.core.event-vector-sources-read-request.v1";
}
