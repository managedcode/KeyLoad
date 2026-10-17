namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>First native source-signed issuer request retained before dispatch, including actual source proof ACK.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveReceiverIssuePacketAlias)]
internal sealed record PartitionMoveReceiverIssuePacket(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] Guid IssuanceNonce,
    [property: Orleans.Id(3)] ReadOnlyMemory<byte> OriginalRequestBytes,
    [property: Orleans.Id(4)] string OriginalRequestSignature);
