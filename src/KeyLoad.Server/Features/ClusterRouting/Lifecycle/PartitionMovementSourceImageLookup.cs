using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Checks fresh authority and the retained image under the original source registry gate.</summary>
internal sealed class PartitionMovementSourceImageLookup(Lock gate,
    Dictionary<Guid, IPartitionMovementRetainedSourceImage> sessions,
    Action<PartitionRef, Guid> requireMoveOpen, TimeProvider clock)
{
    private const string Unavailable = "The partition movement source capability is unavailable.";

    internal PartitionMovementSourceEntry Find(PrincipalRecord principal, PartitionMovePeerEnvelope verified,
        Guid handleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        lock (gate)
        {
            requireMoveOpen(verified.Partition, verified.MoveId);
            if (!sessions.TryGetValue(handleId, out var retained) || retained is not PartitionMovementSourceEntry entry)
            { throw Errors.Fail(ErrorCode.OwnershipLost, Unavailable); }
            entry.Require(verified, clock.GetUtcNow());
            return entry;
        }
    }
}
