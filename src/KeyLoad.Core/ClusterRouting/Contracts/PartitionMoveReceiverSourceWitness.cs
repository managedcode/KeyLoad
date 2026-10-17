namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Exact first source authority bytes retained before receiver first issuance, never refreshed after unknown dispatch.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveReceiverSourceWitnessAlias)]
internal sealed record PartitionMoveReceiverSourceWitness(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] Guid QueryNonce,
    [property: Orleans.Id(3)] ReadOnlyMemory<byte> OriginalReplyBytes,
    [property: Orleans.Id(4)] string OriginalReplySignature);
