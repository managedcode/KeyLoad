using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ResourceDefinitionBinding operations against the caller's database context.</summary>
public static class ResourceDefinitionBinding
{
    /// <summary>Resolves the current definition and its validated lifetime in the caller's existing execution view.</summary>
    internal static ResourceDefinition BindResourceDefinition(IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind = null) => ReadBoundResourcePair(view, partition, name, kind).Definition;
    internal static (ResourceDefinition Definition, ResourceLifetime Lifetime) ReadBoundResourcePair(IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind)
    {
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        var pair = ReadBoundResourceMetadataPair(view, partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, name, kind);
        if (view is IResourceEffectObservation observation)
        {
            observation.ObserveResource(partition, pair.Lifetime);
        }

        return pair;
    }

    internal static (ResourceDefinition Definition, ResourceLifetime Lifetime) ReadBoundResourceMetadataPair(IKeyValueView view, string tenantId, string databaseId, string transactionDomainId, string name, ResourceKind? kind)
    {
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        var resource = view.GetRecord<ResourceDefinition>(KeySpace.Resource(tenantId, databaseId, name));
        var lifetime = (ResourceLifetimeCatalog.Read(view, tenantId, databaseId, name, resource)).Value;
        if (resource is null)
        {
            throw Errors.Fail(ErrorCode.NotFound, EngineResourceAccess.DatabaseEngineResourceIsNotConfiguredDetail);
        }

        if (resource.TransactionDomainId != transactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, EngineResourceAccess.DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail);
        }

        if (kind is { } required && resource.Kind != required)
        {
            throw Errors.Fail(ErrorCode.Validation, EngineResourceAccess.DatabaseEngineResourceHasADifferentKindDetail);
        }

        return (resource, lifetime!);
    }
}
