using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentPhaseIds
{
    private const int IdentityBytes = 16;
    private const long InitialCleanupGeneration = 0;

    internal static Guid For(PartitionMoveRequest original, string principalId,
        PartitionMovementParentPhaseRole role, int ordinal = PartitionMovementProtocol.InitialPhaseOrdinal, long cleanupGeneration = InitialCleanupGeneration)
    {
        if (original.MoveId == Guid.Empty || original.DestinationPhysicalShardId == Guid.Empty
            || original.ExpectedPlacementRevision < PartitionMovementProtocol.InitialPlacementRevision || string.IsNullOrWhiteSpace(principalId)
            || !Enum.IsDefined(role) || ordinal < PartitionMovementProtocol.InitialPhaseOrdinal || cleanupGeneration < InitialCleanupGeneration
            || cleanupGeneration != InitialCleanupGeneration && role is not (PartitionMovementParentPhaseRole.RetireGrant
                or PartitionMovementParentPhaseRole.Retire or PartitionMovementParentPhaseRole.RetireAcknowledge
                or PartitionMovementParentPhaseRole.RetireCancellation
                or PartitionMovementParentPhaseRole.RetireCancellationObservation))
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        var seed = new PartitionMovementParentPhaseIdentity(PartitionMoveProtocol.Version,
            original with { Mode = PartitionMoveMode.Transfer }, principalId, role, ordinal, cleanupGeneration);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(NativeSerialization.Serialize(seed), digest);
        return new Guid(digest[..IdentityBytes]);
    }
}
