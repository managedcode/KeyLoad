namespace KeyLoad.Clustering;

internal static class NativeAdministratorAuthenticationScope
{
    internal static bool Validate(GrainRequestEnvelope request)
    {
        if (request.Purpose != NativeAdministratorAuthenticationProtocol.Purpose)
        {
            return false;
        }
        if (request.ReadKind != GrainReadKind.Authenticate || request.CommandKind is not null
            || request.CommandId != Guid.Empty || request.PrincipalId is not null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }
}
