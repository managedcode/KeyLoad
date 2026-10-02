using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class GrainRequestAuthority
{
    internal static PrincipalRecord Reload(DatabaseEngine database, string principalId, TimeProvider clock)
    {
        ClusterPrincipalPolicy.RequirePublicCredential(principalId);
        return database.Store.Read(view => database.Principal(view, principalId, clock.GetUtcNow()));
    }

    internal static PrincipalRecord Authenticate(DatabaseEngine database, ReadOnlyMemory<byte> payload, TimeProvider clock)
    {
        var secret = GrainPayloadJson.Read<string>(payload);
        var principalId = database.Authenticate(secret, clock.GetUtcNow());
        return Reload(database, principalId, clock);
    }

    internal static void RequireAdministrator(PrincipalRecord principal)
    {
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
    }
}
