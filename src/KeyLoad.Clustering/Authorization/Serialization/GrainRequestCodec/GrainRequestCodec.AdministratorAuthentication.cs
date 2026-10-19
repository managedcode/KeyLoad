namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateAdministratorAuthentication(Guid requestId,
        AdministratorAuthenticationRequest request)
        => AdministratorAuthenticationRequestIssuance.Create(this, clock, settings, requestId, request);
}
