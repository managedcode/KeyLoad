namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeGraphFixtures.CollectionsAlias)]
internal sealed record NativeGraphCollections(
    [property: global::Orleans.Id(0)] string?[] NullableValues,
    [property: global::Orleans.Id(1)] string[] RequiredValues);
