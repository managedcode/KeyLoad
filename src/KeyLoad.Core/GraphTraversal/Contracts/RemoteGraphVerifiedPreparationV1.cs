namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphVerifiedPreparationV1.Alias)]
internal sealed record RemoteGraphVerifiedPreparationV1(
    [property: global::Orleans.Id(0)] RemoteGraphTargetPreparationV1 Preparation,
    [property: global::Orleans.Id(1)] string Token)
{
    internal const string Alias = "keyload.core.remote-graph.verifiedpreparation.v1";
}
