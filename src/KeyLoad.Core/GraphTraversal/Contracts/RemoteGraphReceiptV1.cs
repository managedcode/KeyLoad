namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiptV1.Alias)]
internal sealed record RemoteGraphReceiptV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphIntentV1 Intent,
    [property: global::Orleans.Id(2)] CommitReceipt TargetReceipt)
{
    internal const string Alias = "keyload.core.remote-graph.receipt.v1";
}
