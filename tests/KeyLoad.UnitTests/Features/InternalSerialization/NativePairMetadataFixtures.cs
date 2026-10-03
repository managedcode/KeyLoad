using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativePairMetadataFixtures
{
    internal const string Alias = "keyload.tests.native-pair-metadata.v1";
    internal const string Key = "pair-key";
    internal const string Value = "pair-value";

    internal static NativePairMetadataRecord Create(bool present, bool empty)
    {
        var pair = new KeyValuePair<string, string>(Key, Value);
        return new(pair, present ? pair : null, empty ? [] : [pair], new(null, null), [new(null, null)]);
    }

    internal static NativePairMetadataRecord RequiredNull(NativePairMetadataShape shape, bool nullKey)
    {
        var pair = new KeyValuePair<string, string>(nullKey ? null! : Key, nullKey ? Value : null!);
        var value = Create(present: true, empty: false);
        return shape switch
        {
            NativePairMetadataShape.Direct => value with { Direct = pair },
            NativePairMetadataShape.Nullable => value with { Optional = pair },
            NativePairMetadataShape.ImmutableArray => value with { Items = [pair] },
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }
}

internal enum NativePairMetadataShape
{
    Direct,
    Nullable,
    ImmutableArray
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativePairMetadataFixtures.Alias)]
internal sealed record NativePairMetadataRecord(
    [property: global::Orleans.Id(0)] KeyValuePair<string, string> Direct,
    [property: global::Orleans.Id(1)] KeyValuePair<string, string>? Optional,
    [property: global::Orleans.Id(2)] ImmutableArray<KeyValuePair<string, string>> Items,
    [property: global::Orleans.Id(3)] KeyValuePair<string?, string?> NullableChildren,
    [property: global::Orleans.Id(4)] ImmutableArray<KeyValuePair<string?, string?>> NullableItems);
