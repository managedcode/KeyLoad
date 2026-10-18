using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static bool IsResourceFreeNativeOperation(OperationKind kind)
        => kind is OperationKind.ConfigurePrincipal or OperationKind.ConfigureApiKey or OperationKind.SetDispatch
            or OperationKind.RuntimeJournal or OperationKind.BootstrapPhysicalShardCatalog
            or OperationKind.RegisterPhysicalOwner or OperationKind.ResourceRetirementPhase;

    private static bool ObserveCatalogResourceEffect(IKeyValueView view, ReplicatedOperation operation)
    {
        switch (operation.Kind)
        {
            case OperationKind.ConfigureResource:
                var configure = Payload<ConfigureResourceRequest>(operation);
                ObserveCatalogLifetime(view, configure.TenantId, configure.DatabaseId, configure.Definition.Name);
                return true;
            case OperationKind.CreateTable:
                var create = Payload<CreateTableRequest>(operation);
                ObserveCatalogLifetime(view, create.TenantId, create.DatabaseId, create.Definition.Name);
                return true;
            case OperationKind.SetQueueDispatch:
                var dispatch = Payload<QueueDispatchControlRequest>(operation);
                ObserveCatalogLifetime(view, dispatch.TenantId, dispatch.DatabaseId, dispatch.Queue);
                return true;
            default:
                return false;
        }
    }

    private static void ObserveCatalogLifetime(IKeyValueView view, string tenant, string database,
        string name, string? domain = null)
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

    private void ObserveEventControlLifetime(IKeyValueView view, EventVectorControlPhase phase)
    {
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        var witness = EventVectorCoverageWitnessEncoding.Read(phase.OriginalCoverageWitness,
            phase.OriginalRequest, admission);
        var lifetime = EventVectorCoverageLifetimeValidation.Require(witness, admission);
        if (view is IResourceEffectObservation observation)
        {
            observation.ObserveLifetime(lifetime);
        }
    }
}
