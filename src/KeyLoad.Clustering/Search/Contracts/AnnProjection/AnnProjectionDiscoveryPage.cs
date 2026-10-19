using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Exposes only bounded ANN hints from the actual execution storage generation.</summary>
[GenerateSerializer, Alias(AnnProjectionDiscoveryPage.SerializerAlias)]
public sealed record AnnProjectionDiscoveryPage([property: Id(0)] ImmutableArray<AnnProjectionWorkerEvent> Hints,
    [property: Id(1)] ImmutableArray<byte>? AfterKey, [property: Id(2)] ImmutableArray<ErrorCode> Rejected)
{
    internal const string SerializerAlias = "keyload.orleans.ann-projection-discovery-page";
}
