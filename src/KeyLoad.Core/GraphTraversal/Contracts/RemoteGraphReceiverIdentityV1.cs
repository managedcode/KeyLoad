namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverIdentityV1.Alias)]
internal sealed record RemoteGraphReceiverIdentityV1(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] RemoteGraphIntentV1 Intent)
{
    internal const string Alias = "keyload.core.remote-graph.receiveridentity.v1";
}
