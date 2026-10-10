namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferPeerProtocol.RemoteTargetAlias)]
internal sealed record RemoteTransferRemoteTarget(
    [property: global::Orleans.Id(RemoteTransferRemoteTargetFields.DestinationOwner)] RegisteredPhysicalOwnerV1 DestinationOwner,
    [property: global::Orleans.Id(RemoteTransferRemoteTargetFields.MaximumReservedReceiptBytes)] int MaximumReservedReceiptBytes)
{
    [global::Orleans.Id(RemoteTransferRemoteTargetFields.OriginalSourceCommit)]
    public CommitToken? OriginalSourceCommit { get; init; }
}
