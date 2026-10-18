using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRetirementTopologyOpen(IKeyValueView view, ReplicatedOperation operation)
    {
        if (operation.Kind is OperationKind.RegisterPhysicalOwner or OperationKind.BootstrapPhysicalShardCatalog)
        {
            ResourceRetirementPreparationStorage.RequireOwnerRegistrationOpen(view, Limits);
        }
    }
}
