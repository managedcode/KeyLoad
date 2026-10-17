namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferPeerProtocol.ProofAlias)]
internal sealed record RemoteTransferNativeProof(
    [property: global::Orleans.Id(RemoteTransferProofFields.OriginalEnvelope)] ReadOnlyMemory<byte> OriginalEnvelope,
    [property: global::Orleans.Id(RemoteTransferProofFields.OriginalSignature)] string OriginalSignature,
    [property: global::Orleans.Id(RemoteTransferProofFields.Call)] RemoteQueueTransferPeerCall Call,
    [property: global::Orleans.Id(RemoteTransferProofFields.TechnicalPrincipalId)] string TechnicalPrincipalId,
    [property: global::Orleans.Id(RemoteTransferProofFields.TechnicalPolicyEpoch)] long TechnicalPolicyEpoch,
    [property: global::Orleans.Id(RemoteTransferProofFields.OriginalEvaluatedAt)] DateTimeOffset OriginalEvaluatedAt);
