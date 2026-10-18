using System.Security.Cryptography;

namespace KeyLoad.Clustering;

/// <summary>Issues bounded native authentication authority and clears the original secret bytes.</summary>
internal static class NativeAdministratorAuthenticationRequestIssuance
{
    internal static string Create(GrainRequestCodec codec,
        TimeProvider clock, GrainRoutingOptions settings, Guid requestId,
        NativeAdministratorAuthenticationRequestV1 request)
    {
        var now = clock.GetUtcNow();
        if (request.ExpiresAt <= now || request.ExpiresAt > now + settings.RequestLifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var bytes = NativeSerialization.Serialize(request);
        try
        {
            return codec.Issue(new GrainRequestEnvelope
            {
                Purpose = NativeAdministratorAuthenticationProtocol.Purpose,
                RequestId = requestId,
                Incarnation = codec.Incarnation,
                PrincipalId = null,
                ReadKind = GrainReadKind.Authenticate,
                Payload = GrainNativePayload.Copy(bytes, codec.Limits.MaxBatchBytes),
                ExpiresAt = request.ExpiresAt
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
