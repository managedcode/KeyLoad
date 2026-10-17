namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphSourceProofV1.Alias)]
internal sealed record RemoteGraphSourceProofV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphIntentV1 Intent,
    [property: global::Orleans.Id(2)] RemoteGraphOwnerCutV1 Cut,
    [property: global::Orleans.Id(3)] string PrincipalId,
    [property: global::Orleans.Id(4)] RemoteGraphSourceIdentityV1 Original)
{
    internal const string Alias = "keyload.core.remote-graph.sourceproof.v1";
}
