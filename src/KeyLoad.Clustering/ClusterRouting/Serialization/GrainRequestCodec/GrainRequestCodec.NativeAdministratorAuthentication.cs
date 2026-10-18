namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateNativeAdministratorAuthentication(Guid requestId,
        NativeAdministratorAuthenticationRequestV1 request)
        => NativeAdministratorAuthenticationRequestIssuance.Create(this, clock, settings, requestId, request);
}
