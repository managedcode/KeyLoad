using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireControlledGrantSnapshot(PartitionMovePeerEnvelope original,
        PartitionMoveAuthorizeBody body, PartitionMovePhaseGrant grant,
        PartitionMovePhaseResult first, PrincipalRecord principal)
    {
        var immutable = grant with { Settlement = null, AbortDisposition = null };
        if (first.Stage != PartitionMovePeerStage.ControlAuthorize || first.MoveId != original.MoveId
            || first.Grant is null || JsonData.Fingerprint(first.Grant) != JsonData.Fingerprint(immutable)
            || grant.OperatorPrincipalId != principal.Id || grant.OperatorPolicyEpoch != principal.PolicyEpoch
            || grant.GrantId != body.GrantId || grant.PhaseCommandId != body.PhaseCommandId
            || grant.Stage != body.Phase.Stage || grant.PageOrdinal != body.Phase.PageOrdinal
            || grant.MoveId != original.MoveId || grant.Partition != original.Partition
            || grant.ExpiresAt != body.ExpiresAt || grant.ControlIntentDigest != original.ControlIntentDigest
            || grant.BodyDigest != Convert.ToHexStringLower(SHA256.HashData(body.Phase.Body.Span))
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, body.ReceiverOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ControlOwner, original.ControlOwner)
            || first.Journal.CommandId != grant.GrantId || first.Journal.AppliedPosition != grant.AdmissionPosition
            || first.Journal.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(first.Journal.PhysicalOwner, grant.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
