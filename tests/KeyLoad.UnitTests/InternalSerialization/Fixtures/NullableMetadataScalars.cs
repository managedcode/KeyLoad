namespace KeyLoad.UnitTests.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NullableMetadataFixtures.ScalarsAlias)]
internal sealed record NullableMetadataScalars(
    [property: global::Orleans.Id(0)] long? Number,
    [property: global::Orleans.Id(1)] DateTimeOffset? Date,
    [property: global::Orleans.Id(2)] ErrorCode? Error);
