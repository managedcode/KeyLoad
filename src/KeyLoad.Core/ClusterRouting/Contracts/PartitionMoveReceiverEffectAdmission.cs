namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Native sealed effect admission; independently verified proofs do not alter original effect identity.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveReceiverEffectAdmissionAlias)]
internal sealed record PartitionMoveReceiverEffectAdmission(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalRequestNonce,
    [property: Orleans.Id(2)] DateTimeOffset OriginalExpiresAt,
    [property: Orleans.Id(3)] PartitionMovePhaseGrant OriginalGrant,
    [property: Orleans.Id(4)] PartitionMoveReceiverIssuanceWitness ReceiverWitness,
    [property: Orleans.Id(5)] PartitionMoveReceiverSourceWitness SourceDispatchWitness);
