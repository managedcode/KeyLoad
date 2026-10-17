namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverStateV1.Alias)]
internal sealed record RemoteGraphReceiverStateV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] GraphCrossPartitionReceiverStateV1 Content,
    [property: global::Orleans.Id(2)] RemoteGraphIntentV1 Intent,
    [property: global::Orleans.Id(3)] RemoteGraphReceiptV1 Receipt)
{
    internal const string Alias = "keyload.core.remote-graph.receiverstate.v1";
}
