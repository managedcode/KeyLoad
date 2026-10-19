namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.RootAlias)]
internal sealed record NativeAnnRootReceipt(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string Root,
    [property: global::Orleans.Id(2)] Guid NodeId,
    [property: global::Orleans.Id(3)] Guid Incarnation,
    [property: global::Orleans.Id(4)] string[] OwnedGenerations,
    [property: global::Orleans.Id(5)] NativeAnnPublished[] Published,
    [property: global::Orleans.Id(6)] NativeAnnPublished[] Pending,
    [property: global::Orleans.Id(7)] NativeAnnOriginalOperation[] OriginalOperations);
