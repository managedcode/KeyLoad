using System.Security.Claims;

namespace KeyLoad.Clustering;

internal static class RequestContextAdmission
{
    internal static void Admit(ClaimsPrincipal? principal, GrainRequestContextState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (state.RequestId == Guid.Empty || state.ConnectionId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        if (principal is not null)
        {
            var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
            GrainIdentityContext.ValidatePrincipal(principal, subject);
        }
    }
}
