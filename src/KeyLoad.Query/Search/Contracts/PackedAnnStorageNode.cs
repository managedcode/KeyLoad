namespace KeyLoad.Query.Search;

[Orleans.GenerateSerializer, Orleans.Alias(PackedAnnStorageAliases.Node)]
internal sealed record PackedAnnStorageNode(
    [property: Orleans.Id(0)] int Ordinal,
    [property: Orleans.Id(1)] string Id,
    [property: Orleans.Id(2)] long Revision,
    [property: Orleans.Id(3)] byte Level,
    [property: Orleans.Id(4)] float[] Vector,
    [property: Orleans.Id(5)] int[][] Neighbors);
