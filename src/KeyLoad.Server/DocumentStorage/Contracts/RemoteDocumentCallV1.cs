using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Server.Features.DocumentStorage;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteDocumentProtocol.CallAlias)]
internal sealed record RemoteDocumentCallV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] string Nonce,
    [property: global::Orleans.Id(3)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(4)] string CallerVoter,
    [property: global::Orleans.Id(5)] string CallerSiloAddress,
    [property: global::Orleans.Id(6)] PhysicalShardRecord Source,
    [property: global::Orleans.Id(7)] RemoteDocumentReadFenceV1 Fence,
    [property: global::Orleans.Id(8)] GetDocumentRequest? Request,
    [property: global::Orleans.Id(9)] PartitionQueryOwnedLeafRequest? QueryLeaf = null,
    [property: global::Orleans.Id(10)] KeyLoad.Query.Features.QueryExecution.DistributedSearchOwnedLeafV1? SearchLeaf = null);
