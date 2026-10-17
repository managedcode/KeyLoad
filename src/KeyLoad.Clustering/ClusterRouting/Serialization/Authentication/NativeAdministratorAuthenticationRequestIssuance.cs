using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Issues bounded native authentication authority and clears the original secret bytes.</summary>
internal static class NativeAdministratorAuthenticationRequestIssuance
{
    internal static string Create(GrainRequestCodec codec, DatabaseEngine database,
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
                Incarnation = database.Store.Identity.Incarnation,
                PrincipalId = null,
                ReadKind = GrainReadKind.Authenticate,
                Payload = GrainNativePayload.Copy(bytes, database.Limits.MaxBatchBytes),
                ExpiresAt = request.ExpiresAt
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
