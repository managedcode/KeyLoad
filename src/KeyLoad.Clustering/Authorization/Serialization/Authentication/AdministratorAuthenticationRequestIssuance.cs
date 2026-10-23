using System.Security.Cryptography;

namespace KeyLoad.Clustering;

/// <summary>Issues bounded execution authentication authority and clears the original secret bytes.</summary>
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
        var bytes = OrleansSerialization.Serialize(request);
        try
        {
            return codec.Issue(new GrainRequestEnvelope
            {
                Purpose = AdministratorAuthenticationProtocol.Purpose,
                RequestId = requestId,
                Incarnation = codec.Incarnation,
                PrincipalId = null,
                ReadKind = GrainReadKind.Authenticate,
                Payload = GrainPayload.Copy(bytes, codec.Limits.MaxBatchBytes),
                ExpiresAt = request.ExpiresAt
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
