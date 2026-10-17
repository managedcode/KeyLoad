using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.Search;

internal sealed record AnnReadLifetimeMetadata(ResourceDefinition Definition,
    ResourceLifetimeCoverageV1 Coverage, PrincipalRecord Principal, ImmutableArray<ResourceDefinition> Definitions)
{
    private const string Invalid = "The ANN source lifetime metadata is invalid.";

    internal void Require(PartitionRef partition, int maximumBytes)
    {
        if (Definition is null || Principal is null || Definitions.IsDefaultOrEmpty
            || Definition.Kind != ResourceKind.Collection || Definition.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
        PartitionControlLifetimeCoverage.Require(Coverage, partition, Definitions, maximumBytes);
        if (NativeSerialization.Measure(Definitions) > maximumBytes || NativeSerialization.Measure(Definition) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Invalid);
        }
        var primary = Definitions.FirstOrDefault(value => value.Name == Definition.Name);
        if (primary is null || !PartitionControlLifetimeProofValidation.Same(primary, Definition))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
    }
}
