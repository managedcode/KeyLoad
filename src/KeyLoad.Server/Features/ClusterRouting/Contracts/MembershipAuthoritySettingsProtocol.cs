namespace KeyLoad.Server.Features.ClusterRouting;

internal static class MembershipAuthoritySettingsProtocol
{
    internal const string Section = "MembershipAuthority";
    internal const string Local = "local";
    internal const string Authority = "authority";
    internal const string Proxy = "proxy";
    internal const string Invalid = "Membership authority settings are invalid.";
    internal const int RequiredMembers = 3;
    internal const int SecretBytes = 32;
    internal const int MaximumIdentityBytes = 256;
    internal const int NativeSiloPort = 11_111;
}
