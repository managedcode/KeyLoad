namespace KeyLoad.Server.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProtocol.RootAlias)]
internal sealed record AnnRootReceipt(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string Root,
    [property: global::Orleans.Id(2)] Guid NodeId,
    [property: global::Orleans.Id(3)] Guid Incarnation,
    [property: global::Orleans.Id(4)] string[] OwnedGenerations,
    [property: global::Orleans.Id(5)] AnnPublished[] Published,
    [property: global::Orleans.Id(6)] AnnPublished[] Pending,
    [property: global::Orleans.Id(7)] AnnOriginalOperation[] OriginalOperations);
