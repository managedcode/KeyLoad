using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Resolves the current definition and its validated lifetime in the caller's existing native view.</summary>
    internal static ResourceDefinition BindResourceDefinition(IKeyValueView view, PartitionRef partition,
        string name, ResourceKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        var resource = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, name));
        _ = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, name, resource);
        if (resource is null)
        {
            throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
        }
        if (resource.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail);
        }
        if (kind is { } required && resource.Kind != required)
        {
            throw Errors.Fail(ErrorCode.Validation, DatabaseEngineResourceHasADifferentKindDetail);
        }
        return resource;
    }
}
