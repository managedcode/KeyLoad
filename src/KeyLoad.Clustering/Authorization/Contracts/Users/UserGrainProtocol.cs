namespace KeyLoad.Clustering;

internal static class UserGrainProtocol
{
    internal const string GrainAlias = "keyload.user";
    internal const int InterfaceVersion = 1;
    internal const string AuthenticationAlias = "keyload.user.credential-authentication";
    internal const string AuthenticateAlias = "authenticate";
    internal const string InvalidateAlias = "invalidate";
}
