namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferAcceptFailureClaimsFields.Alias)]
internal sealed record RemoteTransferAcceptFailureClaims(
    [property: global::Orleans.Id(RemoteTransferAcceptFailureClaimsFields.Purpose)] string Purpose,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureClaimsFields.OriginalAuthority)] RemoteTransferAcceptFailureAuthority OriginalAuthority,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureClaimsFields.OutcomeDigest)] string OutcomeDigest,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureClaimsFields.CurrentDependency)] RemoteTransferAcceptDependency CurrentDependency,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureClaimsFields.TargetCut)] CommitToken TargetCut,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureClaimsFields.ReadGeneration)] long ReadGeneration,
    [property: global::Orleans.Id(RemoteTransferAcceptFailureClaimsFields.FailureKind)] RemoteTransferAcceptFailureKind FailureKind);
