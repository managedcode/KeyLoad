using KeyLoad.Orleans;
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
    [property: global::Orleans.Id(9)] PartitionQueryOwnedLeafRequest? QueryLeaf = null);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteDocumentProtocol.ReplyAlias)]
internal sealed record RemoteDocumentReplyV1(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] string Nonce,
    [property: global::Orleans.Id(2)] OwnedDocumentReadResultV1? Result,
    [property: global::Orleans.Id(3)] ErrorCode? Error,
    [property: global::Orleans.Id(4)] string? SafeDetail,
    [property: global::Orleans.Id(5)] ReplicaSiloDiscovery EndpointDiscovery,
    [property: global::Orleans.Id(6)] PartitionQueryLeafResultV1? QueryLeaf = null,
    [property: global::Orleans.Id(7)] ControlledDocumentReadResult? Controlled = null,
    [property: global::Orleans.Id(8)] ControlledBlobReadResult? ControlledBlob = null);
