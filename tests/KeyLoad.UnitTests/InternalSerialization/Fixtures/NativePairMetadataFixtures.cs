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
