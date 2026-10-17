namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeNullableMetadataFixtures.ScalarsAlias)]
internal sealed record NativeNullableMetadataScalars(
    [property: global::Orleans.Id(0)] long? Number,
    [property: global::Orleans.Id(1)] DateTimeOffset? Date,
    [property: global::Orleans.Id(2)] ErrorCode? Error);
