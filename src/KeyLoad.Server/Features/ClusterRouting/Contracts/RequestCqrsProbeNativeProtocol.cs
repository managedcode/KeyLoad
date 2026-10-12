namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeNativeProtocol
{
    internal const string ClaimAlias = "keyload.request-cqrs-probe-native-claim.v1";
    internal const string IdentityAlias = "keyload.request-cqrs-probe-native-identity.v1";
    internal const string MethodAlias = "keyload.request-cqrs-probe-native-method.v1";
    internal const string ReplyAlias = ReplyPurpose;
    internal const string RequestAlias = RequestPurpose;
    internal const int Version = 1;
    internal const int FixedMembers = 3;
    internal const string ServiceAlias = "keyload.request-cqrs-probe-grain-service.v1";
    internal const string RequestPurpose = "keyload.request-cqrs-probe-native-request.v1";
    internal const string ReplyPurpose = "keyload.request-cqrs-probe-native-reply.v1";
    internal const string Unavailable = "The original producer disposal notification is unavailable.";
}
