namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphCompletionClaimsV1.Alias)]
internal sealed record RemoteGraphCompletionClaimsV1(
    [property: global::Orleans.Id(0)] string Purpose,
    [property: global::Orleans.Id(1)] RemoteGraphReceiptV1 Receipt,
    [property: global::Orleans.Id(2)] string SourcePrincipalId,
    [property: global::Orleans.Id(3)] long SourcePolicyEpoch,
    [property: global::Orleans.Id(4)] DateTimeOffset ExpiresAt)
{
    internal const string Alias = "keyload.core.remote-graph.completionclaims.v1";
}
