using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;

namespace KeyLoad.Server;

internal static class DatabaseAuthenticationTicketObservation
{
    internal static DatabaseAuthenticationTicketSnapshot Capture(AuthenticationTicket ticket, string runtimeSubject)
        => new(ticket.AuthenticationScheme, ticket.Principal.Identities.Select(identity =>
            new DatabaseAuthenticationIdentitySnapshot(identity.IsAuthenticated, identity.AuthenticationType,
                identity.NameClaimType, identity.RoleClaimType, identity.Actor is not null,
                identity.BootstrapContext is not null, identity.Label is not null,
                identity.Claims.Select(claim => new DatabaseAuthenticationClaimSnapshot(claim.Type,
                    claim.ValueType, claim.Issuer, claim.OriginalIssuer, claim.Properties.Count,
                    string.Equals(claim.Value, runtimeSubject, StringComparison.Ordinal))).ToImmutableArray()))
            .ToImmutableArray());
}
