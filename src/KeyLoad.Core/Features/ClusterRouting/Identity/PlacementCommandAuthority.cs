using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static AtomicPartitionPlacementResolution AuthorizePlacementCommand(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        RequirePlacementAdministrator(principal);
        var control = operation.Kind switch
        {
            OperationKind.ConfigurePlacementPolicy => Payload<ConfigurePlacementPolicyRequest>(operation).ControlPartition,
            OperationKind.PersistPlacementIntent => Payload<PlacementIntentPersistenceAdmissionV1>(operation).OriginalRequest.ControlPartition,
            OperationKind.PlacementIntentPhase => Payload<PlacementIntentPhaseRequestV1>(operation).ControlPartition,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, TenantPlacementPolicyProtocol.Invalid)
        };
        ValidatePartition(control);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        RequirePlacementControlPartition(view, control, control.TenantId, control.DatabaseId, catalog.DefaultShard);
        return ResolveRegisteredPlacement(view, control, catalog.DefaultShard);
    }
}
