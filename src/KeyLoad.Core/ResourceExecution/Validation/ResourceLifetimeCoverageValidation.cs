using System.Collections.Immutable;
using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.ResourceExecution;

internal static class ResourceLifetimeCoverageValidation
{
    internal static ResourceLifetimeCoverage Capture(IKeyValueView view, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions)
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
        Require(result, partition, definitions);
        return result;
    }

    internal static void Require(ResourceLifetimeCoverage coverage, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions)
    {
        if (coverage is null || coverage.Partition != partition || coverage.Lifetimes.IsDefault
            || definitions.IsDefault || coverage.Lifetimes.Length != definitions.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceLifetimeValidationMessages.MissingAuthority);
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < definitions.Length; index++)
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
    }

}
