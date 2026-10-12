namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalGroupTrustProtocol
{
    internal const string Section = "PhysicalGroupTrust";
    internal const string Invalid = "Physical group trust settings are invalid.";
    internal const int RequiredMembers = 3;
    internal const int SecretBytes = 32;
    internal const int MaximumIdentityBytes = 256;
    internal const int EncodedSecretCharacters = 44;
}
