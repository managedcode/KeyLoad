namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Only original authenticated pending scope; actual receiver principal/epoch are captured natively.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveReceiverIssueBodyAlias)]
internal sealed record PartitionMoveReceiverIssueBody(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: Orleans.Id(3)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> SourcePendingProof,
    [property: Orleans.Id(5)] string? ActualReceiverPrincipalId = null,
    [property: Orleans.Id(6)] long ActualReceiverPolicyEpoch = PartitionMoveParentContractNames.UnissuedPolicyEpoch,
    [property: Orleans.Id(7)] string SourcePendingSignature = PartitionMoveParentContractNames.AbsentSignature,
    [property: Orleans.Id(8)] ReadOnlyMemory<byte> OriginalIssuerRequestBytes = default,
    [property: Orleans.Id(9)] string OriginalIssuerRequestSignature = PartitionMoveParentContractNames.AbsentSignature);
