using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionControlLifetimeCoverage
{
    private const int FirstResourceIndex = 0;

    internal static ResourceLifetimeCoverageV1 Capture(IKeyValueView view, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions, int maximumBytes)
    {
        if (definitions.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
        }
        var values = ImmutableArray.CreateBuilder<ResourceLifetimeV1>(definitions.Length);
        foreach (var definition in definitions)
        {
            values.Add(ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId,
                definition.Name, definition) ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority));
        }
        var result = new ResourceLifetimeCoverageV1(partition, values.MoveToImmutable());
        Require(result, partition, definitions, maximumBytes);
        return result;
    }

    internal static void Require(ResourceLifetimeCoverageV1 coverage, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions, int maximumBytes)
    {
        if (coverage is null || coverage.Partition != partition || coverage.Lifetimes.IsDefault
            || definitions.IsDefault || coverage.Lifetimes.Length != definitions.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = FirstResourceIndex; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            if (definition is null || definition.TransactionDomainId != partition.TransactionDomainId
                || !names.Add(definition.Name) || coverage.Lifetimes[index] is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
            }
            ResourceLifetimeIdentity.Validate(coverage.Lifetimes[index], partition.TenantId,
                partition.DatabaseId, definition);
        }
        if (NativeSerialization.Measure(coverage) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity);
        }
    }

    internal static void RequireCurrent(IKeyValueView view, ResourceLifetimeCoverageV1 coverage,
        ImmutableArray<ResourceDefinition> definitions, int maximumBytes)
    {
        Require(coverage, coverage.Partition, definitions, maximumBytes);
        foreach (var definition in definitions)
        {
            var actual = view.GetRecord<ResourceDefinition>(KeySpace.Resource(coverage.Partition.TenantId,
                coverage.Partition.DatabaseId, definition.Name));
            if (actual is null || !PartitionControlLifetimeProofValidation.Same(actual, definition))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
            }
        }
        var current = Capture(view, coverage.Partition, definitions, maximumBytes);
        if (!NativeSerialization.Serialize(current).AsSpan().SequenceEqual(NativeSerialization.Serialize(coverage)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        }
    }
}
