using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NullableMetadataFixtures.CollectionsAlias)]
internal sealed record NullableMetadataCollections(
    [property: global::Orleans.Id(0)] ImmutableArray<string>? Values,
    [property: global::Orleans.Id(1)] ImmutableArray<ImmutableArray<string>?>? Nested);
