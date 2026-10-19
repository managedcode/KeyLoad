using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class GrainRequestScope
{
    internal static void RequireRequestIdentity(GrainRequestEnvelope request, Guid requestId)
    {
        if (request.RequestId != requestId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }

    internal static void Validate(GrainRequestEnvelope request, Guid incarnation, DateTimeOffset now, TimeSpan maximumFuture)
    {
        if (request.Incarnation != incarnation
            || request.RequestId == Guid.Empty || request.ExpiresAt <= now || request.ExpiresAt > now + maximumFuture
            || (request.ReadKind is null) == (request.CommandKind is null) || request.Payload.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        if (request.ReadKind is { } read && (!Enum.IsDefined(read) || request.CommandId != Guid.Empty)
            || request.CommandKind is { } command && (!Enum.IsDefined(command) || request.CommandId == Guid.Empty))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        if (!AdministratorAuthenticationScope.Validate(request)
            && !TextMaintenanceChildScope.Validate(request) && !MultiLaneReceiveChildScope.Validate(request)
            && !AnnMaintenanceChildScope.Validate(request)
            && !Bm25RequestScope.Validate(request) && !AnnProjectionObservationScope.Validate(request)
            && !OnlineTextRequestScope.Validate(request)
            && !QueryLifetimeReadScope.Validate(request))
        {
            RuntimeJournalRequestScope.Validate(request);
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
