namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphPreparationClaimsV1.Alias)]
internal sealed record RemoteGraphPreparationClaimsV1(
    [property: global::Orleans.Id(0)] string Purpose,
    [property: global::Orleans.Id(1)] RemoteGraphTargetPreparationV1 Preparation,
    [property: global::Orleans.Id(2)] string SourcePrincipalId,
    [property: global::Orleans.Id(3)] long SourcePolicyEpoch,
    [property: global::Orleans.Id(4)] string OriginalRequestDigest,
    [property: global::Orleans.Id(5)] DateTimeOffset ExpiresAt)
{
    internal const string Alias = "keyload.core.remote-graph.preparationclaims.v1";
}
