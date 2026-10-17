namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphCompletionResultV1.Alias)]
internal sealed record RemoteGraphCompletionResultV1(
    [property: global::Orleans.Id(0)] CommitReceipt Receipt,
    [property: global::Orleans.Id(1)] RemoteGraphCompletionMutationV1 Original)
{
    internal const string Alias = "keyload.core.remote-graph.completionresult.v1";
}
