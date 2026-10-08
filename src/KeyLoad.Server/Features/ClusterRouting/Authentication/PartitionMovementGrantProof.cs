using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Checks the exact committed grant after the original configured A peer MAC is verified.</summary>
internal static class PartitionMovementGrantProof
{
    private const string InvalidProof = "The partition movement peer proof is invalid.";

    internal static void Require(PartitionMovementTransportRequest request, PhysicalShardRecord control,
        PhysicalShardRecord receiver, DateTimeOffset now)
    {
        var envelope = request.Envelope;
        PartitionMoveGrantValidation.Require(envelope, request.CommandId, receiver, now);
        var grant = envelope.Grant!;
        var receipt = request.ControlGrantJournal;
        if (request.CommandId == Guid.Empty || receipt is null
            || !PhysicalOwnerEntryValidation.SameOwner(envelope.ControlOwner, control)
            || receipt.CommandId != grant.GrantId || receipt.AppliedPosition != grant.AdmissionPosition
            || receipt.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, control))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        JsonData.Identifier(grant.OperatorPrincipalId);
    }
}
