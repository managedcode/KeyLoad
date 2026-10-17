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

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphCompletionMutationV1.Alias)]
internal sealed record RemoteGraphCompletionMutationV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid CommandId,
    [property: global::Orleans.Id(2)] RemoteGraphReceiptV1 Receipt,
    [property: global::Orleans.Id(3)] string VerifiedToken)
{
    internal const string Alias = "keyload.core.remote-graph.completionmutation.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphCapacityV1.Alias)]
internal sealed record RemoteGraphCapacityV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string Direction,
    [property: global::Orleans.Id(2)] long RecordCount,
    [property: global::Orleans.Id(3)] long EncodedBytes)
{
    internal const string Alias = "keyload.core.remote-graph.capacity.v1";
}
