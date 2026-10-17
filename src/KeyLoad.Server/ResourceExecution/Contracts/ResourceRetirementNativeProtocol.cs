namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Freezes the private native retirement envelope without defining its Core phases.</summary>
internal static class ResourceRetirementNativeProtocol
{
    internal const int Version = 1;
    internal const string ServiceMethodAlias = "execute-resource-retirement";
    internal const string RequestAlias = "keyload.resource-execution.resource-retirement-native-request.v1";
    internal const string PhaseAlias = "keyload.resource-execution.resource-retirement-native-phase-request.v1";
    internal const string ReplyAlias = "keyload.resource-execution.resource-retirement-native-reply.v1";
    internal const string RequestPurpose = "keyload-resource-retirement-native-request-v1";
    internal const string ReplyPurpose = "keyload-resource-retirement-native-reply-v1";
    internal const string Unavailable = "The original bounded resource retirement is unavailable.";
    internal const string InvalidProof = "The resource retirement envelope is invalid.";
}
