namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverResultV1.Alias)]
internal sealed record RemoteGraphReceiverResultV1(
    [property: global::Orleans.Id(0)] RemoteGraphReceiptV1 Receipt,
    [property: global::Orleans.Id(1)] RemoteGraphReceiverMutationV1 Original)
{
    internal const string Alias = "keyload.core.remote-graph.receiverresult.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphCompletionResultV1.Alias)]
internal sealed record RemoteGraphCompletionResultV1(
    [property: global::Orleans.Id(0)] CommitReceipt Receipt,
    [property: global::Orleans.Id(1)] RemoteGraphCompletionMutationV1 Original)
{
    internal const string Alias = "keyload.core.remote-graph.completionresult.v1";
}
