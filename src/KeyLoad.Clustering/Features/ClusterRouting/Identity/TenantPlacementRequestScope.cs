using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Clustering;

internal static class TenantPlacementRequestScope
{
    internal static bool Validate(GrainRequestEnvelope request)
    {
        var capacity = request.ReadKind == GrainReadKind.OwnerCapacity;
        var phase = request.CommandKind == OperationKind.PlacementIntentPhase;
        if (request.Purpose == TenantPlacementCapacityProtocol.ReadPurpose || capacity)
        {
            if (!capacity || request.Purpose != TenantPlacementCapacityProtocol.ReadPurpose
                || request.CommandKind is not null || request.CommandId != Guid.Empty)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
            return true;
        }
        if (request.Purpose == PlacementIntentNativeProtocol.Purpose || phase)
        {
            if (!phase || request.Purpose != PlacementIntentNativeProtocol.Purpose
                || request.ReadKind is not null || request.CommandId == Guid.Empty)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
            return true;
        }
        return false;
    }
}
