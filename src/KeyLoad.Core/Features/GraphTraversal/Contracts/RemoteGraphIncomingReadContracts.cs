using System.Collections.Immutable;

namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverReceiptReadRequestV1.Alias)]
internal sealed record RemoteGraphReceiverReceiptReadRequestV1(
    [property: global::Orleans.Id(0)] RemoteGraphIntentV1 Intent,
    [property: global::Orleans.Id(1)] Guid OriginalReceiveCommandId)
{
    internal const string Alias = "keyload.core.remote-graph.receiverreceiptreadrequest.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphIncomingSourceProofRequestV1.Alias)]
internal sealed record RemoteGraphIncomingSourceProofRequestV1(
    [property: global::Orleans.Id(0)] RemoteGraphIntentV1 Intent)
{
    internal const string Alias = "keyload.core.remote-graph.incomingsourceproofrequest.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphIncomingSourceProofV1.Alias)]
internal sealed record RemoteGraphIncomingSourceProofV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphIntentV1 Intent,
    [property: global::Orleans.Id(2)] EdgeRecord? Edge,
    [property: global::Orleans.Id(3)] RemoteGraphOwnerCutV1 SourceCut)
{
    internal const string Alias = "keyload.core.remote-graph.incomingsourceproof.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverCandidatesRequestV1.Alias)]
internal sealed record RemoteGraphReceiverCandidatesRequestV1(
    [property: global::Orleans.Id(0)] EntityRef Target,
    [property: global::Orleans.Id(1)] string Graph,
    [property: global::Orleans.Id(2)] int Limit)
{
    internal const string Alias = "keyload.core.remote-graph.receivercandidatesrequest.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphReceiverCandidatesV1.Alias)]
internal sealed record RemoteGraphReceiverCandidatesV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] RemoteGraphReceiverCandidatesRequestV1 Request,
    [property: global::Orleans.Id(2)] ImmutableArray<RemoteGraphReceiverStateV1> States,
    [property: global::Orleans.Id(3)] RemoteGraphOwnerCutV1 TargetCut)
{
    internal const string Alias = "keyload.core.remote-graph.receivercandidates.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphIncomingFinalizeRequestV1.Alias)]
internal sealed record RemoteGraphIncomingFinalizeRequestV1(
    [property: global::Orleans.Id(0)] RemoteGraphReceiverCandidatesV1 Candidates,
    [property: global::Orleans.Id(1)] ImmutableArray<RemoteGraphIncomingSourceProofV1> Proofs)
{
    internal const string Alias = "keyload.core.remote-graph.incomingfinalizerequest.v1";
}

