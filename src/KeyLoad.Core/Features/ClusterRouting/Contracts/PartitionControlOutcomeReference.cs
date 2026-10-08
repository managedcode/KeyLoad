namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.OutcomeReferenceAlias)]
internal sealed record PartitionControlOutcomeReference(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PartitionControlCommandIdentity Identity,
    [property: Orleans.Id(2)] string Fingerprint,
    [property: Orleans.Id(3)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> OutcomeKey,
    [property: Orleans.Id(5)] string OutcomeDigest);
