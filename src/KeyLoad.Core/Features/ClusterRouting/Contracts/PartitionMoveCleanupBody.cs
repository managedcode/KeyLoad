namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CleanupRoleAlias)]
internal enum PartitionMoveCleanupRole { Source = 1, Target = 2 }

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CleanupBodyAlias)]
internal sealed record PartitionMoveCleanupBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveCleanupRole Role,
    [property: Orleans.Id(3)] int FamilyOrdinal,
    [property: Orleans.Id(4)] Guid PrecedingGrantId,
    [property: Orleans.Id(5)] PartitionMovePublishedPlacement? Publication = null);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.CompletionBodyAlias)]
internal sealed record PartitionMoveCompletionBody(
    [property: Orleans.Id(0)] string OperatorPrincipalId,
    [property: Orleans.Id(1)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(2)] PartitionMoveJournalReceipt SourceSettlement,
    [property: Orleans.Id(3)] PartitionMoveJournalReceipt? TargetSettlement,
    [property: Orleans.Id(4)] Guid SourceGrantId,
    [property: Orleans.Id(5)] Guid? TargetGrantId,
    [property: Orleans.Id(6)] ReadOnlyMemory<byte> SourceBody,
    [property: Orleans.Id(7)] ReadOnlyMemory<byte> TargetBody);
