namespace KeyLoad.Query.Search;

[Orleans.GenerateSerializer, Orleans.Alias(PackedAnnStorageAliases.Header)]
internal sealed record PackedAnnStorageHeader(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] VectorSpace Space,
    [property: Orleans.Id(2)] PackedAnnOptions Policy,
    [property: Orleans.Id(3)] int Count,
    [property: Orleans.Id(4)] int EntryPoint,
    [property: Orleans.Id(5)] int MaximumLevel,
    [property: Orleans.Id(6)] long RetainedBytes,
    [property: Orleans.Id(7)] long BuildScratchBytes);
