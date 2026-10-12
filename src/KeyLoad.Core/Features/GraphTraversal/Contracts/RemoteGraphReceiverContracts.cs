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

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverMutationV1.Alias)]
internal sealed record RemoteGraphReceiverMutationV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid CommandId,
    [property: global::Orleans.Id(2)] RemoteGraphSourceProofV1 SourceProof,
    [property: global::Orleans.Id(3)] string VerifiedToken)
{
    internal const string Alias = "keyload.core.remote-graph.receivermutation.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiptV1.Alias)]
internal sealed record RemoteGraphReceiptV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphIntentV1 Intent,
    [property: global::Orleans.Id(2)] CommitReceipt TargetReceipt)
{
    internal const string Alias = "keyload.core.remote-graph.receipt.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverStateV1.Alias)]
internal sealed record RemoteGraphReceiverStateV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] GraphCrossPartitionReceiverStateV1 Content,
    [property: global::Orleans.Id(2)] RemoteGraphIntentV1 Intent,
    [property: global::Orleans.Id(3)] RemoteGraphReceiptV1 Receipt)
{
    internal const string Alias = "keyload.core.remote-graph.receiverstate.v1";
}
