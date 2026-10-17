namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.JournalReceiptAlias)]
internal sealed record PartitionMoveJournalReceipt(
    [property: Orleans.Id(0)] Guid CommandId,
    [property: Orleans.Id(1)] PhysicalShardRecord PhysicalOwner,
    [property: Orleans.Id(2)] long AppliedPosition,
    [property: Orleans.Id(3)] string ControlIntentDigest,
    [property: Orleans.Id(4)] string? EffectDigest = null);
