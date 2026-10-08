using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireOriginalMoveOutcomeReceiver(PartitionMovePeerEnvelope original,
        PhysicalShardRecord receiver)
    {
        if (PartitionMoveGrantValidation.IsLocalControl(original.Stage))
        {
            if (original.Grant is not null
                || !PhysicalOwnerEntryValidation.SameOwner(original.ControlOwner, receiver))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            return;
        }
        var grant = original.Grant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        if (grant.MoveId != original.MoveId || grant.Partition != original.Partition
            || grant.Stage != original.Stage || grant.PageOrdinal != original.PageOrdinal
            || grant.ControlIntentDigest != original.ControlIntentDigest
            || grant.ExpiresAt != original.ExpiresAt
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ControlOwner, original.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, receiver)
            || grant.BodyDigest != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(original.Body.Span)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
