namespace KeyLoad.UnitTests.InternalSerialization;

internal static class PairMetadataFixtures
{
    internal const string Alias = "keyload.tests.execution-pair-metadata";
    internal const string Key = "pair-key";
    internal const string Value = "pair-value";

    internal static PairMetadataRecord Create(bool present, bool empty)
    {
        var pair = new KeyValuePair<string, string>(Key, Value);
        return new(pair, present ? pair : null, empty ? [] : [pair], new(null, null), [new(null, null)]);
    }

    internal static PairMetadataRecord RequiredNull(PairMetadataShape shape, bool nullKey)
    {
        var pair = new KeyValuePair<string, string>(nullKey ? null! : Key, nullKey ? Value : null!);
        var value = Create(present: true, empty: false);
        return shape switch
        {
            PairMetadataShape.Direct => value with { Direct = pair },
            PairMetadataShape.Nullable => value with { Optional = pair },
            PairMetadataShape.ImmutableArray => value with { Items = [pair] },
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }
}
