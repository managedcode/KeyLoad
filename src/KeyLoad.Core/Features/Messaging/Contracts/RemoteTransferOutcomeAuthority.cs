namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferPeerProtocol.AuthorityAlias)]
internal sealed record RemoteTransferOutcomeAuthority(
    [property: global::Orleans.Id(RemoteTransferAuthorityFields.LogicalPrincipalId)] string LogicalPrincipalId,
    [property: global::Orleans.Id(RemoteTransferAuthorityFields.LogicalPolicyEpoch)] long LogicalPolicyEpoch,
    [property: global::Orleans.Id(RemoteTransferAuthorityFields.FieldHeaderDigest)] string FieldHeaderDigest,
    [property: global::Orleans.Id(RemoteTransferAuthorityFields.SourceOwner)] RegisteredPhysicalOwnerV1 SourceOwner,
    [property: global::Orleans.Id(RemoteTransferAuthorityFields.DestinationOwner)] RegisteredPhysicalOwnerV1 DestinationOwner,
    [property: global::Orleans.Id(RemoteTransferAuthorityFields.TechnicalPrincipalId)] string TechnicalPrincipalId,
    [property: global::Orleans.Id(RemoteTransferAuthorityFields.TechnicalPolicyEpoch)] long TechnicalPolicyEpoch,
    [property: global::Orleans.Id(RemoteTransferAuthorityFields.ProofDigest)] string ProofDigest);
