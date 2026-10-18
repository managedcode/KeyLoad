using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static class AnnProjectionPinValidation
{
    private const string OwnerLost = "The ANN maintenance owner no longer matches the canonical placement.";
    private const string InvalidScope = "The ANN projection pin does not cover its canonical source.";
    private static readonly string[] Kinds = [MutationDiscriminatorNames.PutDocument,
        MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument,
        MutationDiscriminatorNames.PutVector, MutationDiscriminatorNames.ApplyVectorProjection];

    internal static long Require(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, PartitionRef partition,
        AnnMaintenanceRequest request)
    {
        var consumer = request.Consumer;
        var state = DatabaseEngine.RequireActiveProjectionConsumer(view, principal, consumer, request.IndexGeneration);
        RequireOwner(database, view, principal, partition, request);
        RequireMaintenanceProfile(database, state, principal, request);
        if (consumer.Partition != partition || !state.Definition.Resources.IsEmpty
            || !state.Definition.MutationKinds.SequenceEqual(Kinds))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidScope);
        }
        return state.Checkpoint;
    }
    private static void RequireMaintenanceProfile(DatabaseEngine database, ProjectionConsumerInfo state,
        PrincipalRecord principal, AnnMaintenanceRequest request)
    {
        if (state.Definition.AnnMaintenance != request.MaintenanceProfile)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidScope);
        }
        if (state.Definition.AnnMaintenance is null)
        {
            if (state.MaintenanceAuthority is not null)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidScope);
            }
            return;
        }
        var authority = state.MaintenanceAuthority;
        if (authority is null || authority.ConfigureCommandId == Guid.Empty
            || authority.PrincipalId != principal.Id || authority.PolicyEpoch != principal.PolicyEpoch
            || authority.Incarnation != database.Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidScope);
        }
    }

    internal static void RequireOwner(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, PartitionRef partition, AnnMaintenanceRequest request)
    {
        var actual = database.ReadAtomicPartitionPlacement(view, principal.Id, partition);
        var expected = request.Placement;
        if (database.Store.Identity.NodeId != request.NodeId
            || database.Store.Identity.Incarnation != expected.Incarnation
            || actual.PhysicalShardId != expected.PhysicalShardId || actual.Incarnation != expected.Incarnation
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OwnerLost);
        }
    }

}
