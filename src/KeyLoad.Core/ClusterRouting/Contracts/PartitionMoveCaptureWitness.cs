namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Retains the exact first authenticated Capture reply; its nonce is checked against prior durable admission.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveCaptureWitnessAlias)]
internal sealed record PartitionMoveCaptureWitness(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] Guid OriginalRequestNonce,
    [property: Orleans.Id(3)] ReadOnlyMemory<byte> OriginalReplyBytes,
    [property: Orleans.Id(4)] string OriginalReplySignature);
