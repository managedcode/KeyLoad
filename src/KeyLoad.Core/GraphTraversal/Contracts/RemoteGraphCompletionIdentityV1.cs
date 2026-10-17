namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphCompletionIdentityV1.Alias)]
internal sealed record RemoteGraphCompletionIdentityV1(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] RemoteGraphReceiptV1 Receipt)
{
    internal const string Alias = "keyload.core.remote-graph.completionidentity.v1";
}
