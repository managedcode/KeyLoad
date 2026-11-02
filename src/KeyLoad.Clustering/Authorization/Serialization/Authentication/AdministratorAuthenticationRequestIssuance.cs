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
            Payload /* KLD0041: Preserve the current generated Payload field ID and runtime type header at this request envelope boundary. */ = GrainPayload.Strict(request)
        });
}
