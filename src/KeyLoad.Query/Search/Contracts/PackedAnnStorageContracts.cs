namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnStorageAliases
{
    internal const string Policy = "keyload.search.packed-ann-policy.v1";
    internal const string Header = "keyload.search.packed-ann-header.v1";
    internal const string Node = "keyload.search.packed-ann-node.v1";
}

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

[Orleans.GenerateSerializer, Orleans.Alias(PackedAnnStorageAliases.Node)]
internal sealed record PackedAnnStorageNode(
    [property: Orleans.Id(0)] int Ordinal,
    [property: Orleans.Id(1)] string Id,
    [property: Orleans.Id(2)] long Revision,
    [property: Orleans.Id(3)] byte Level,
    [property: Orleans.Id(4)] float[] Vector,
    [property: Orleans.Id(5)] int[][] Neighbors);
