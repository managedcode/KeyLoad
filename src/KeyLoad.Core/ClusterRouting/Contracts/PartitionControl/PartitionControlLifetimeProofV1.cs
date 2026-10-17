namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Retains the original command delegation together with its admitted lifetime coverage.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionResourceLifetimeContract.ControlProofAlias)]
internal sealed record PartitionControlLifetimeProofV1(
    [property: Orleans.Id(PartitionResourceLifetimeContract.ControlDelegationId)] PartitionControlDelegation OriginalDelegation,
    [property: Orleans.Id(PartitionResourceLifetimeContract.ControlCoverageId)] ResourceLifetimeCoverageV1 Coverage);
