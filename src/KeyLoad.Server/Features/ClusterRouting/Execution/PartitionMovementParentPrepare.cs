using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentPrepare
{
    internal static PartitionMovePhaseCommand Create(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state)
    {
        if (request.Mode != PartitionMoveMode.Transfer || state.Header is not null || state.Control is not null
            || state.Placement.Revision != request.ExpectedPlacementRevision)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var destination = state.Directory.Owners.SingleOrDefault(owner =>
            owner.Owner.PhysicalShardId == request.DestinationPhysicalShardId)?.Owner
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        var body = NativeSerialization.Serialize(new PartitionMovePrepareBody(principalId, request, RequireParentCheckpoint: true));
        return new(PartitionMoveProtocol.Version, request.MoveId, request.Partition, state.Directory.ControlOwner,
            state.Placement, destination, Convert.ToHexStringLower(SHA256.HashData(body)),
            PartitionMovePeerStage.ControlPrepare, PartitionMovementProtocol.InitialPhaseOrdinal, body, Resources: ImmutableArray<ResourceDefinition>.Empty);
    }
}
