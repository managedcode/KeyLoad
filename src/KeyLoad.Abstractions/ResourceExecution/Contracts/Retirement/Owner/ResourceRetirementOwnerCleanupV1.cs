using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementOwnerCleanupAlias)]
internal sealed record ResourceRetirementOwnerCleanupV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver,
    [property: Orleans.Id(2)] ImmutableArray<ResourceRetirementCleanupCheckpointV1> Partitions,
    [property: Orleans.Id(3)] ResourceRetirementPhaseReceiptV1 Receipt)
{
    [Orleans.Id(4)]
    public ResourceRetirementOutcomeIdentityV1? OutcomeIdentity
    {
        get; init;
    }
}
