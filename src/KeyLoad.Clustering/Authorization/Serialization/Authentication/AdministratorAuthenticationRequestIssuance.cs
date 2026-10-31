namespace KeyLoad.Clustering;

/// <summary>Issues bounded execution authentication authority without copying the secret into another buffer.</summary>
internal static class AdministratorAuthenticationRequestIssuance
{
    internal static GrainRequestEnvelope Create(GrainRequestCodec codec,
        TimeProvider clock, GrainRoutingOptions settings, Guid requestId,
        AdministratorAuthenticationRequest request)
    {
        var now = clock.GetUtcNow();
        if (request.ExpiresAt <= now || request.ExpiresAt > now + settings.RequestLifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return codec.Issue(new GrainRequestEnvelope
        {
            Purpose = AdministratorAuthenticationProtocol.Purpose,
            RequestId = requestId,
            Incarnation = codec.Incarnation,
            PrincipalId = null,
            ReadKind = GrainReadKind.Authenticate,
            Payload = GrainPayload.Strict(request),
            ExpiresAt = request.ExpiresAt
        });
    }
}
