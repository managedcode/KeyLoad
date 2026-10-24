using KeyLoad.Core.InternalSerialization;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ConfiguredResourceOutcome operations against the caller's database context.</summary>
public static class ConfiguredResourceOutcome
{
    internal const string ConfiguredOutcomeLifetimeChanged = "The configured resource belongs to a different lifetime.";
    internal static void ObserveConfiguredResourceOutcome(this DatabaseEngine engine, IKeyValueView view, Command<DatabaseCommand> operation, Result<object> result, ReadExecutionBudget work)
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
            var request = OperationProtocol.Payload<ConfigureResourceRequest>(operation);
            (tenantId, databaseId, resourceName) = (request.TenantId, request.DatabaseId, request.Definition.Name);
        }
        else
        {
            var request = OperationProtocol.Payload<CreateTableRequest>(operation);
            (tenantId, databaseId, resourceName) = (request.TenantId, request.DatabaseId, request.Definition.Name);
        }

        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(tenantId, databaseId, resourceName));
        var lifetime = ResourceLifetimeCatalog.Read(view, tenantId, databaseId, resourceName, definition) ?? throw Errors.Fail(ErrorCode.NotFound, EngineResourceAccess.DatabaseEngineResourceIsNotConfiguredDetail);
        var admitted = ResourceEffectAdmissionValidation.Read(operation, engine.Limits);
        var sameLifetime = admitted.Bindings.IsEmpty ? lifetime.CreationFingerprint == DatabaseCommandFingerprint.Compute(operation) : admitted.Bindings.Contains(lifetime);
        if (!sameLifetime)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ConfiguredOutcomeLifetimeChanged);
        }

        work.ObserveAuthenticatedResourceLifetimes([lifetime]);
    }
}
