namespace KeyLoad.Server.Features.ClusterRouting;

internal static class CommandObservationNativeProtocol
{
    internal const int Version = 1;
    internal const string MethodAlias = "keyload.command-observation.native-exchange.v1";
    internal const string ActionAlias = "keyload.command-observation.native-action.v1";
    internal const string ReplyAlias = "keyload.command-observation.native-reply.v1";
    internal const string RequestAlias = "keyload.command-observation.native-request.v1";
    internal const string RequestPurpose = "keyload-command-observation-native-request-v1";
    internal const string ReplyPurpose = "keyload-command-observation-native-reply-v1";
    internal const string Invalid = "The original command observation transport is invalid.";
    internal const string Unavailable = "The original command observation owner is unavailable.";
}
