namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverClaimsV1.Alias)]
internal sealed record RemoteGraphReceiverClaimsV1(
    [property: global::Orleans.Id(0)] string Purpose,
    [property: global::Orleans.Id(1)] RemoteGraphSourceProofV1 SourceProof,
    [property: global::Orleans.Id(2)] string TargetPrincipalId,
    [property: global::Orleans.Id(3)] long TargetPolicyEpoch,
    [property: global::Orleans.Id(4)] DateTimeOffset ExpiresAt)
{
    internal const string Alias = "keyload.core.remote-graph.receiverclaims.v1";
}
