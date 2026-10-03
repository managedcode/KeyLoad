using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class GrainRequestScope
{
    internal static void Validate(GrainRequestEnvelope request, Guid incarnation, DateTimeOffset now)
    {
        if (request.Purpose != GrainNativeContracts.RequestPurpose || request.Incarnation != incarnation
            || request.RequestId == Guid.Empty || request.ExpiresAt <= now || request.ExpiresAt > now + GrainRoutingProtocol.MaximumFuture
            || (request.ReadKind is null) == (request.CommandKind is null) || request.Payload.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        if (request.ReadKind is { } read && (!Enum.IsDefined(read) || request.CommandId != Guid.Empty)
            || request.CommandKind is { } command && (!Enum.IsDefined(command) || request.CommandId == Guid.Empty))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        if (request.CommandKind == OperationKind.Membership || request.PrincipalId == ClusterPrincipalPolicy.InternalPrincipalId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }

        if (request.ReadKind == GrainReadKind.Authenticate)
        {
            if (request.PrincipalId is not null)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(request.PrincipalId))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }

        JsonData.Identifier(request.PrincipalId);
    }
}
