using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Orleans;

internal static class RemoteTransferRequestScope
{
    internal static bool Validate(GrainRequestEnvelope request)
    {
        if (request.Purpose != RemoteTransferPeerProtocol.GrainPurpose)
        { return false; }
        if (!(request.CommandKind == OperationKind.Batch && request.ReadKind is null
            || request.ReadKind == GrainReadKind.QueueTransferCoordination && request.CommandKind is null))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return true;
    }
}
