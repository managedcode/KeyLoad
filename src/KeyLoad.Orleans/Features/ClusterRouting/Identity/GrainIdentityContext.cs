using System.Security.Claims;
using KeyLoad.Core;
using ManagedCode.Orleans.Identity.Core.Constants;
using ManagedCode.Orleans.Identity.Core.Extensions;

namespace KeyLoad.Orleans;

internal static class GrainIdentityContext
{
    internal static ClaimsPrincipal CreatePrincipal(string principalId)
    {
        JsonData.Identifier(principalId);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, principalId)], GrainRequestStreamProtocol.AuthenticationType);
        return new ClaimsPrincipal(identity);
    }

    internal static void Validate(GrainRequestEnvelope envelope, Guid actorRequestId)
    {
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState state
            || state.RequestId != actorRequestId || state.RequestId != envelope.RequestId
            || state.CommandId != envelope.CommandId || state.ConnectionId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        var value = RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS);
        if (envelope.ReadKind == GrainReadKind.Authenticate)
        {
            if (envelope.PrincipalId is not null || value is not null
                || RequestContext.Keys.Contains(OrleansIdentityConstants.USER_CLAIMS, StringComparer.Ordinal))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
            }

            return;
        }

        if (value is not ClaimsPrincipal principal || envelope.PrincipalId is not { } subject)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }

        ValidatePrincipal(principal, subject);
        if (!ReferenceEquals(OrleansIdentityContext.RequireAuthenticatedPrincipal(), principal)
            || OrleansIdentityContext.RequireAuthenticatedUserId() != subject)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }
    }

    internal static void ValidateConnection(GrainRequestEnvelope envelope, Guid connectionId)
    {
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState state
            || connectionId == Guid.Empty || state.ConnectionId != connectionId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        Validate(envelope, envelope.RequestId);
    }

    internal static void ValidatePrincipal(ClaimsPrincipal principal, string subject)
    {
        const int EmptyPropertiesCount = 0;

        ArgumentNullException.ThrowIfNull(principal);
        JsonData.Identifier(subject);
        using var identities = principal.Identities.GetEnumerator();
        if (!identities.MoveNext())
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }

        var identity = identities.Current;
        if (identities.MoveNext() || !identity.IsAuthenticated
            || identity.AuthenticationType != GrainRequestStreamProtocol.AuthenticationType
            || identity.NameClaimType != ClaimsIdentity.DefaultNameClaimType
            || identity.RoleClaimType != ClaimsIdentity.DefaultRoleClaimType || identity.Actor is not null
            || identity.BootstrapContext is not null || identity.Label is not null)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }

        using var claims = identity.Claims.GetEnumerator();
        if (!claims.MoveNext())
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }

        var claim = claims.Current;
        if (claims.MoveNext() || claim.Type != ClaimTypes.NameIdentifier || claim.Value != subject
            || claim.ValueType != ClaimValueTypes.String || claim.Issuer != ClaimsIdentity.DefaultIssuer
            || claim.OriginalIssuer != ClaimsIdentity.DefaultIssuer || claim.Properties.Count != EmptyPropertiesCount)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }
    }

}
