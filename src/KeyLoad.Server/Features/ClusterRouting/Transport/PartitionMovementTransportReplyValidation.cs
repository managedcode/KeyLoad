using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Validates the existing authenticated native transport reply value shape.</summary>
internal static class PartitionMovementTransportReplyValidation
{
    internal static void RequireValue(GrainOperationReply reply)
    {
        if (reply.Error is { } code)
        {
            if (!Enum.IsDefined(code) || !reply.Payload.IsEmpty
                || reply.SafeDetail != PartitionMovementProtocol.Unavailable)
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        else if (reply.Payload.IsEmpty || reply.SafeDetail is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }
}
