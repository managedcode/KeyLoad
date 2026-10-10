namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferPeerProtocol.CallAlias)]
internal sealed record RemoteQueueTransferPeerCall(
    [property: global::Orleans.Id(RemoteTransferCallFields.Version)] int Version,
    [property: global::Orleans.Id(RemoteTransferCallFields.RequestId)] Guid RequestId,
    [property: global::Orleans.Id(RemoteTransferCallFields.Nonce)] string Nonce,
    [property: global::Orleans.Id(RemoteTransferCallFields.ExpiresAt)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(RemoteTransferCallFields.CallerVoter)] string CallerVoter,
    [property: global::Orleans.Id(RemoteTransferCallFields.CallerSiloAddress)] string CallerSiloAddress,
    [property: global::Orleans.Id(RemoteTransferCallFields.SourceOwner)] RegisteredPhysicalOwnerV1 SourceOwner,
    [property: global::Orleans.Id(RemoteTransferCallFields.DestinationOwner)] RegisteredPhysicalOwnerV1 DestinationOwner,
    [property: global::Orleans.Id(RemoteTransferCallFields.Stage)] RemoteQueueTransferPeerStage Stage,
    [property: global::Orleans.Id(RemoteTransferCallFields.OriginalCommandId)] Guid OriginalCommandId,
    [property: global::Orleans.Id(RemoteTransferCallFields.LogicalPrincipalId)] string LogicalPrincipalId,
    [property: global::Orleans.Id(RemoteTransferCallFields.LogicalPolicyEpoch)] long LogicalPolicyEpoch,
    [property: global::Orleans.Id(RemoteTransferCallFields.FieldHeaderDigest)] string FieldHeaderDigest,
    [property: global::Orleans.Id(RemoteTransferCallFields.IntentToken)] string IntentToken,
    [property: global::Orleans.Id(RemoteTransferCallFields.IntentClaims)] RemoteTransferIntentClaims IntentClaims,
    [property: global::Orleans.Id(RemoteTransferCallFields.MaximumReplyBytes)] int MaximumReplyBytes,
    [property: global::Orleans.Id(RemoteTransferCallFields.SourceCut)] CommitToken SourceCut);
