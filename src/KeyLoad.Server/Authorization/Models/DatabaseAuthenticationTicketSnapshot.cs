using System.Collections.Immutable;

namespace KeyLoad.Server;

internal sealed record DatabaseAuthenticationTicketSnapshot(string Scheme,
    ImmutableArray<DatabaseAuthenticationIdentitySnapshot> Identities);
