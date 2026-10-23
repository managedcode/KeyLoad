using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ResourceMetadataScopes operations against the caller's database context.</summary>
public static class ResourceMetadataScopes
{
    internal static bool IsResourceFreeOperation(OperationKind kind) => kind is OperationKind.ConfigurePrincipal or OperationKind.ConfigureApiKey or OperationKind.SetDispatch or OperationKind.RuntimeJournal;
    internal static bool ObserveCatalogResourceEffect(IKeyValueView view, Command<DatabaseCommand> operation)
    {
        switch (operation.Value!.Kind)
        {
            case OperationKind.ConfigureResource:
                var configure = OperationProtocol.Payload<ConfigureResourceRequest>(operation);
                ObserveCatalogLifetime(view, configure.TenantId, configure.DatabaseId, configure.Definition.Name);
                return true;
            case OperationKind.CreateTable:
                var create = OperationProtocol.Payload<CreateTableRequest>(operation);
                ObserveCatalogLifetime(view, create.TenantId, create.DatabaseId, create.Definition.Name);
                return true;
            case OperationKind.DropTable:
                var drop = OperationProtocol.Payload<DropTableRequest>(operation);
                ObserveCatalogLifetime(view, drop.TenantId, drop.DatabaseId, drop.ResourceName);
                return true;
            case OperationKind.SetQueueDispatch:
                var dispatch = OperationProtocol.Payload<QueueDispatchControlRequest>(operation);
                ObserveCatalogLifetime(view, dispatch.TenantId, dispatch.DatabaseId, dispatch.Queue);
                return true;
            default:
                return false;
        }
    }

    internal static void ObserveCatalogLifetime(IKeyValueView view, string tenant, string database, string name, string? domain = null)
    {
        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(tenant, database, name));
        var lifetime = ResourceLifetimeCatalog.Read(view, tenant, database, name, definition);
        if (lifetime is null)
        {
            if (domain is not null)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceEffectAdmissionProtocol.Unbound);
            }

            return;
        }

        if (domain is not null && lifetime.TransactionDomainId != domain)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }

        if (view is IResourceEffectObservation observation)
        {
            observation.ObserveLifetime(lifetime);
        }
    }
}
