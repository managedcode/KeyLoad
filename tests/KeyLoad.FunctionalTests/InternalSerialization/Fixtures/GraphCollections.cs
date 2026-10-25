namespace KeyLoad.FunctionalTests.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(GraphFixtures.CollectionsAlias)]
internal sealed record GraphCollections(
    [property: global::Orleans.Id(0)] string?[] NullableValues,
    [property: global::Orleans.Id(1)] string[] RequiredValues);
