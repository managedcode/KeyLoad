using System.Security.Claims;
using KeyLoad.Core;
using ManagedCode.Orleans.Identity.Core.Constants;
using ManagedCode.Orleans.Identity.Core.Extensions;

namespace KeyLoad.Clustering;

internal static class GrainIdentityContext
{
    internal static SiloAddress RequireOriginSilo()
        => (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) /* KLD0041: Inspect the exact native Orleans identity context value before validating the claims principal and request ownership. */) is GrainRequestContextState { OriginSilo: { } address }
            ? address : throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);

    internal static ClaimsPrincipal CreatePrincipal(string principalId)
    {
        JsonData.Identifier(principalId);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, principalId)], GrainRequestStreamProtocol.AuthenticationType);
        return new ClaimsPrincipal(identity);
    }

    internal static void Validate(GrainRequestEnvelope envelope, Guid actorRequestId)
    {
        if (((RequestContext.Get(GrainRequestStreamProtocol.ContextKey) /* KLD0041: Inspect the exact native Orleans identity context value before validating the claims principal and request ownership. */) is not GrainRequestContextState state /* KLD0042: Validate the native Orleans RequestContext object as KeyLoad.Clustering.GrainRequestContextState before using call-local identity or owner state. */)
            || state.RequestId != actorRequestId || state.RequestId != envelope.RequestId
            || state.CommandId != envelope.CommandId || state.ConnectionId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        var /* KLD0041: Inspect the exact native Orleans identity context value before validating the claims principal and request ownership. */ value = (RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS) /* KLD0041: Inspect the exact native Orleans identity context value before validating the claims principal and request ownership. */);
        if (envelope.ReadKind == GrainReadKind.Authenticate)
        {
            if (envelope.PrincipalId is not null || value is not null
                || RequestContext.Keys.Contains(OrleansIdentityConstants.USER_CLAIMS, StringComparer.Ordinal))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
            }

            return;
        }

        if ((value is not ClaimsPrincipal principal /* KLD0042: Validate the native Orleans identity context value as System.Security.Claims.ClaimsPrincipal before accepting the authenticated principal. */) || envelope.PrincipalId is not { } subject)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }

        ValidatePrincipal(principal, subject);
        if (!(ReferenceEquals(OrleansIdentityContext.RequireAuthenticatedPrincipal(), principal) /* KLD0041: Require the authenticated ambient principal to be the exact verified principal instance through CLR reference identity. */)
            || OrleansIdentityContext.RequireAuthenticatedUserId() != subject)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.MissingPrincipal);
        }
    }

    internal static void ValidateConnection(GrainRequestEnvelope envelope, Guid connectionId)
    {
        if (((RequestContext.Get(GrainRequestStreamProtocol.ContextKey) /* KLD0041: Inspect the exact native Orleans identity context value before validating the claims principal and request ownership. */) is not GrainRequestContextState state /* KLD0042: Validate the native Orleans RequestContext object as KeyLoad.Clustering.GrainRequestContextState before using call-local identity or owner state. */)
            || connectionId == Guid.Empty || state.ConnectionId != connectionId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        Validate(envelope, envelope.RequestId);
    }

    internal static void ValidatePrincipal(ClaimsPrincipal principal, string subject)
    {
        const int EmptyPropertiesCount = 0;

        ArgumentNullException.ThrowIfNull(principal) /* KLD0041: BCL null validation accepts the principal ClaimsPrincipal reference through its object parameter. */;
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
            || (identity.BootstrapContext /* KLD0041: The CLR ClaimsIdentity bootstrap context is object-valued; this verified principal contract requires it to be absent. */) is not null || identity.Label is not null)
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
