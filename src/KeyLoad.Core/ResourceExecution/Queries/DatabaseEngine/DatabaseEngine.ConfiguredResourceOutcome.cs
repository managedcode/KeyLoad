using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string ConfiguredOutcomeLifetimeChanged = "The configured resource belongs to a different lifetime.";

    private void ObserveConfiguredResourceOutcome(IKeyValueView view, Command<DatabaseCommand> operation,
        Result<object> result, ReadExecutionBudget work)
    {
        if (operation.Value!.Kind is not (OperationKind.ConfigureResource or OperationKind.CreateTable) || Errors.Code(result.Problem) is not null)
        {
            return;
        }
        string tenantId;
        string databaseId;
        string resourceName;
        if (operation.Value!.Kind == OperationKind.ConfigureResource)
        {
            var request = Payload<ConfigureResourceRequest>(operation);
            (tenantId, databaseId, resourceName) = (request.TenantId, request.DatabaseId, request.Definition.Name);
        }
        else
        {
            var request = Payload<CreateTableRequest>(operation);
            (tenantId, databaseId, resourceName) = (request.TenantId, request.DatabaseId, request.Definition.Name);
        }
        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(tenantId, databaseId, resourceName));
        var lifetime = ResourceLifetimeCatalog.Read(view, tenantId, databaseId, resourceName, definition)
            ?? throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
        var admitted = ResourceEffectAdmissionValidation.Read(operation, Limits);
        var sameLifetime = admitted.Bindings.IsEmpty
            ? lifetime.CreationFingerprint == DatabaseCommandFingerprint.Compute(operation)
            : admitted.Bindings.Contains(lifetime);
        if (!sameLifetime)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ConfiguredOutcomeLifetimeChanged);
        }
        work.ObserveAuthenticatedResourceLifetimes([lifetime]);
    }
}
