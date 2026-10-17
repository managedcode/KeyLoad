namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.DocumentCommandContextAlias)]
internal sealed record PartitionControlDocumentCommandContext(
    [property: Orleans.Id(0)] PartitionMoveControlRecord Control,
    [property: Orleans.Id(1)] AtomicPartitionPlacementResolution Destination,
    [property: Orleans.Id(2)] string OperatorPrincipalId,
    [property: Orleans.Id(3)] PartitionControlCommandIdentity Identity,
    [property: Orleans.Id(4)] PartitionControlCommandRecord? Admission,
    [property: Orleans.Id(5)] OperationResult? OriginalOutcome);
