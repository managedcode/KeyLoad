namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeActivationProtocol
{
    internal const string Kind = "CommandActivation";
    internal const string Prefix = "activation-";
    internal const string Separator = "-";
    internal const int DigestCharacters = 64;
    internal const int SingleMarker = 1;
    internal const int IdentityParts = 2;
    internal const int FirstPart = 0;
    internal const int SecondPart = 1;
}
