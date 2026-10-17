using System.Collections.Immutable;
namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativePairMetadataFixtures.Alias)]
internal sealed record NativePairMetadataRecord(
    [property: global::Orleans.Id(0)] KeyValuePair<string, string> Direct,
    [property: global::Orleans.Id(1)] KeyValuePair<string, string>? Optional,
    [property: global::Orleans.Id(2)] ImmutableArray<KeyValuePair<string, string>> Items,
    [property: global::Orleans.Id(3)] KeyValuePair<string?, string?> NullableChildren,
    [property: global::Orleans.Id(4)] ImmutableArray<KeyValuePair<string?, string?>> NullableItems);
