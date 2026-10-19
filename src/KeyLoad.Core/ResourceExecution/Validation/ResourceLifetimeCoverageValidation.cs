using System.Collections.Immutable;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceLifetimeCoverageValidation
{
    private const int FirstResourceIndex = 0;

    internal static ResourceLifetimeCoverage Capture(IKeyValueView view, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions, int maximumBytes)
    {
        if (definitions.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceLifetimeValidationMessages.Invalid);
        }
        var values = ImmutableArray.CreateBuilder<ResourceLifetime>(definitions.Length);
        foreach (var definition in definitions)
        {
            values.Add(ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId,
                definition.Name, definition) ?? throw Errors.Fail(ErrorCode.Corruption, ResourceLifetimeValidationMessages.MissingAuthority));
        }
        var result = new ResourceLifetimeCoverage(partition, values.MoveToImmutable());
        Require(result, partition, definitions, maximumBytes);
        return result;
    }

    internal static void Require(ResourceLifetimeCoverage coverage, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions, int maximumBytes)
    {
        if (coverage is null || coverage.Partition != partition || coverage.Lifetimes.IsDefault
            || definitions.IsDefault || coverage.Lifetimes.Length != definitions.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceLifetimeValidationMessages.MissingAuthority);
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = FirstResourceIndex; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            if (definition is null || definition.TransactionDomainId != partition.TransactionDomainId
                || !names.Add(definition.Name) || coverage.Lifetimes[index] is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceLifetimeValidationMessages.Invalid);
            }
            ResourceLifetimeIdentity.Validate(coverage.Lifetimes[index], partition.TenantId,
                partition.DatabaseId, definition);
        }
        if (OrleansSerialization.Measure(coverage) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceLifetimeValidationMessages.Capacity);
        }
    }

}
