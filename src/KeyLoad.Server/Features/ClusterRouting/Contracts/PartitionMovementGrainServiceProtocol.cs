namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementGrainServiceProtocol
{
    internal const ushort InterfaceVersion = 1;
    internal const string ServiceAlias = "keyload.partition-movement-grain-service.v1";
    internal const string ExchangeAlias = "exchange";
    internal const string DiscoverAlias = "discover";
    internal const string ProbeOwnerAlias = "probe-owner";
    internal const string ChannelAlias = "keyload.partition-movement-grain-service-channel.v1";
    internal const string RequestAlias = "keyload.partition-movement-grain-service-request.v1";
    internal const string ReplyAlias = "keyload.partition-movement-grain-service-reply.v1";
    internal const string DiscoveryAlias = "keyload.partition-movement-grain-service-discovery.v1";
    internal const string NativeRequestPurpose = "keyload-partition-movement-native-request-v1";
    internal const string NativeReplyPurpose = "keyload-partition-movement-native-reply-v1";
    internal const int PhaseChannel = 0;
    internal const int OutcomeChannel = 1;
    internal const int ReceiverIssueChannel = 2;
    internal const int RetireCancellationChannel = 3;
    internal const int TransferDataChannel = 4;
    internal const int PhysicalOwnerProbeChannel = 5;
    internal const uint RequestChannel = 0;
    internal const uint RequestQuery = 1;
    internal const uint RequestPayload = 2;
    internal const uint RequestSignature = 3;
    internal const uint RequestCallerVoter = 4;
    internal const uint RequestCallerSiloAddress = 5;
    internal const uint RequestDestinationVoter = 6;
    internal const uint RequestDestinationSiloAddress = 7;
    internal const uint RequestNonce = 8;
    internal const uint RequestExpiresAt = 9;
    internal const uint RequestSenderSignature = 10;
    internal const uint ReplyPayload = 0;
    internal const uint ReplySignature = 1;
    internal const uint ReplyError = 2;
    internal const uint ReplySafeDetail = 3;
    internal const uint ReplyNonce = 4;
    internal const uint ReplySenderSignature = 5;
    internal const uint DiscoveryPayload = 0;
    internal const uint DiscoverySignature = 1;
}
