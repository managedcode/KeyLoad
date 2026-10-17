namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverMutationV1.Alias)]
internal sealed record RemoteGraphReceiverMutationV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid CommandId,
    [property: global::Orleans.Id(2)] RemoteGraphSourceProofV1 SourceProof,
    [property: global::Orleans.Id(3)] string VerifiedToken)
{
    internal const string Alias = "keyload.core.remote-graph.receivermutation.v1";
}
