namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.GrantAckBodyAlias)]
internal sealed record PartitionMoveAcknowledgeBody(
    [property: Orleans.Id(0)] Guid GrantId,
    [property: Orleans.Id(1)] PartitionMoveJournalReceipt Settlement);
