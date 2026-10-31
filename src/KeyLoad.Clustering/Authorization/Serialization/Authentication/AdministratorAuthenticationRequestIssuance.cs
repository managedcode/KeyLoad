namespace KeyLoad.Clustering;

/// <summary>Issues bounded execution authentication authority without copying the secret into another buffer.</summary>
internal static class AdministratorAuthenticationRequestIssuance
{
    internal static GrainRequestEnvelope Create(GrainRequestCodec codec, Guid requestId,
        AdministratorAuthenticationRequest request)
        => codec.Issue(new GrainRequestEnvelope
        {
            Purpose = AdministratorAuthenticationProtocol.Purpose,
            RequestId = requestId,
            Incarnation = codec.Incarnation,
            PrincipalId = null,
            ReadKind = GrainReadKind.Authenticate,
            Payload = GrainPayload.Strict(request)
        });
}
