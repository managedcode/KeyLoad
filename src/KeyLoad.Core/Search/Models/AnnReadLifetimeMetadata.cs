using System.Collections.Immutable;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core.Features.Search;

internal sealed record AnnReadLifetimeMetadata(ResourceDefinition Definition,
    ResourceLifetimeCoverage Coverage, PrincipalRecord Principal, ImmutableArray<ResourceDefinition> Definitions)
{
    private const string Invalid = "The ANN source lifetime metadata is invalid.";

    internal void Require(PartitionRef partition, int maximumBytes)
    {
        if (Definition is null || Principal is null || Definitions.IsDefaultOrEmpty
            || Definition.Kind != ResourceKind.Collection || Definition.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
        ResourceLifetimeCoverage.Require(Coverage, partition, Definitions, maximumBytes);
        if (OrleansSerialization.Measure(Definitions) > maximumBytes || OrleansSerialization.Measure(Definition) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Invalid);
        }
        var primary = Definitions.FirstOrDefault(value => value.Name == Definition.Name);
        if (primary is null || !OrleansSerialization.Serialize(primary).AsSpan().SequenceEqual(OrleansSerialization.Serialize(Definition)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
    }
}
