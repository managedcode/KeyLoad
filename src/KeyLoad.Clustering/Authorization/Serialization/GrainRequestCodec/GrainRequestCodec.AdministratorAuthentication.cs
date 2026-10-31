namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateAdministratorAuthentication(Guid requestId,
        AdministratorAuthenticationRequest request)
        => AdministratorAuthenticationRequestIssuance.Create(this, requestId, request);
}
