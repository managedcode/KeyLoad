namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferPeerProtocol.RemoteOriginAlias)]
internal sealed record RemoteTransferRemoteOrigin(
    [property: global::Orleans.Id(RemoteTransferRemoteOriginFields.SourceOwner)] RegisteredPhysicalOwnerV1 SourceOwner,
    [property: global::Orleans.Id(RemoteTransferRemoteOriginFields.LogicalPrincipalId)] string LogicalPrincipalId,
    [property: global::Orleans.Id(RemoteTransferRemoteOriginFields.IntentDigest)] string IntentDigest);
