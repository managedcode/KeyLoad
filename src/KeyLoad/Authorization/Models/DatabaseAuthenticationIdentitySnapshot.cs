using System.Collections.Immutable;

namespace KeyLoad.Server;

internal sealed record DatabaseAuthenticationIdentitySnapshot(bool IsAuthenticated, string? AuthenticationType,
    string NameClaimType, string RoleClaimType, bool HasActor, bool HasBootstrapContext, bool HasLabel,
    ImmutableArray<DatabaseAuthenticationClaimSnapshot> Claims);
