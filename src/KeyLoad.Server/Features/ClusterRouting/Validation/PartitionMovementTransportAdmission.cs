using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementTransportAdmission
{
    private const int FirstOrdinal = 0;
    private const string InvalidProof = "The partition movement peer proof is invalid.";

    internal static void Require(PartitionMovementTransportRequest request)
    {
        if (!Enum.IsDefined(request.Action))
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
        if (request.Action == PartitionMovementTransportAction.Apply)
        {
            if (request.Envelope.Stage == PartitionMovePeerStage.Capture
                || request.HandleId != Guid.Empty || request.Ordinal != FirstOrdinal)
            { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
            return;
        }
        if (request.Envelope.Stage != PartitionMovePeerStage.Capture || request.Ordinal < FirstOrdinal
            || request.Action == PartitionMovementTransportAction.Capture
                && (request.HandleId != Guid.Empty || request.Ordinal != FirstOrdinal)
            || request.Action is PartitionMovementTransportAction.Page or PartitionMovementTransportAction.Release
                && request.HandleId == Guid.Empty
            || request.Action == PartitionMovementTransportAction.Release && request.Ordinal != FirstOrdinal)
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
    }
}
