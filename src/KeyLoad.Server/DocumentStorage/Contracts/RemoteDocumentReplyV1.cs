using KeyLoad.Clustering;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Server.Features.DocumentStorage;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteDocumentProtocol.ReplyAlias)]
internal sealed record RemoteDocumentReplyV1(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] string Nonce,
    [property: global::Orleans.Id(2)] OwnedDocumentReadResultV1? Result,
    [property: global::Orleans.Id(3)] ErrorCode? Error,
    [property: global::Orleans.Id(4)] string? SafeDetail,
    [property: global::Orleans.Id(5)] ReplicaSiloDiscovery EndpointDiscovery,
    [property: global::Orleans.Id(6)] PartitionQueryLeafResultV1? QueryLeaf = null,
    [property: global::Orleans.Id(9)] KeyLoad.Query.Features.QueryExecution.DistributedSearchLeafResultV1? SearchLeaf = null,
    [property: global::Orleans.Id(KeyLoad.Core.Features.Messaging.RemoteTransferPeerProtocol.ReplyField)]
    KeyLoad.Core.Features.Messaging.RemoteQueueTransferPeerResult? QueueTransfer = null);
