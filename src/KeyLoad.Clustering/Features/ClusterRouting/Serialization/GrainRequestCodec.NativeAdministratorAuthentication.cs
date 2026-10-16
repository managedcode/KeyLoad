using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateNativeAdministratorAuthentication(Guid requestId,
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
            return Issue(new GrainRequestEnvelope
            {
                Purpose = NativeAdministratorAuthenticationProtocol.Purpose,
                RequestId = requestId,
                Incarnation = database.Store.Identity.Incarnation,
                PrincipalId = null,
                ReadKind = GrainReadKind.Authenticate,
                Payload = Encode(bytes),
                ExpiresAt = request.ExpiresAt
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
