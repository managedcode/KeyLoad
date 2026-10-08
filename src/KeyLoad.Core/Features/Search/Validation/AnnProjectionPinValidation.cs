using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static class AnnProjectionPinValidation
{
    private const int PlacementVersion = 1;
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
        if (consumer.Partition != partition || !state.Definition.Resources.IsEmpty
            || !state.Definition.MutationKinds.SequenceEqual(Kinds))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidScope);
        }
        return state.Checkpoint;
    }
    internal static void RequireOwner(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, PartitionRef partition, AnnMaintenanceRequest request)
    {
        var actual = database.ReadAtomicPartitionPlacement(view, principal.Id, new(PlacementVersion, partition));
        var expected = request.Placement;
        if (database.Store.Identity.NodeId != request.NodeId
            || database.Store.Identity.Incarnation != expected.Incarnation
            || actual.PhysicalShardId != expected.PhysicalShardId || actual.Incarnation != expected.Incarnation
            || actual.PlacementEpoch != expected.PlacementEpoch
            || !actual.VoterIds.SequenceEqual(expected.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OwnerLost);
        }
    }

}
