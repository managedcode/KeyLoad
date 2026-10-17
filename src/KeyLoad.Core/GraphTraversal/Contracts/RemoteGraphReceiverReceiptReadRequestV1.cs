namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverReceiptReadRequestV1.Alias)]
internal sealed record RemoteGraphReceiverReceiptReadRequestV1(
    [property: global::Orleans.Id(0)] RemoteGraphIntentV1 Intent,
    [property: global::Orleans.Id(1)] Guid OriginalReceiveCommandId)
{
    internal const string Alias = "keyload.core.remote-graph.receiverreceiptreadrequest.v1";
}
