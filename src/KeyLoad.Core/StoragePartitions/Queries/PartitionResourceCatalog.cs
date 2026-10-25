using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.StoragePartitions.Queries;

/// <summary>Reads the resource definitions owned by one atomic partition.</summary>
internal static class PartitionResourceCatalog
{
    private const string Inconsistent = "The partition resource catalog is inconsistent.";

    internal static ImmutableArray<ResourceDefinition> Capture(IKeyValueView view,
        PartitionRef partition)
    {
        var resources = ImmutableArray.CreateBuilder<ResourceDefinition>();
        _ = view.VisitAll(KeySpace.ResourcePrefix(partition.TenantId, partition.DatabaseId), (key, value) =>
        {
            var definition = OrleansSerialization.Deserialize<ResourceDefinition>(value);
            if (!key.SequenceEqual(KeySpace.Resource(partition.TenantId, partition.DatabaseId, definition.Name)))
            {
                throw Errors.Fail(ErrorCode.Corruption, Inconsistent);
            }
            if (definition.TransactionDomainId == partition.TransactionDomainId)
            {
                resources.Add(definition);
            }
            return true;
        });
        return resources.ToImmutable();
    }
}
