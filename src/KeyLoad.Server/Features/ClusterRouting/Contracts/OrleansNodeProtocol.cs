namespace KeyLoad.Server;

internal static class OrleansNodeProtocol
{
    internal const string ServiceId = "KeyLoad";
    internal const string RoutingUnavailable = "The Orleans routing layer is not ready.";
    internal const string SiloAlreadyStarted = "The Orleans silo has already started or stopped in this process.";
    internal const string InvalidSiloAddress = "The silo address must resolve to a routable address on the configured cluster network.";
    internal const string ReplyRejected = "The database operation was rejected.";
    internal const string GuidFormat = "N";
    internal const int GatewayPort = 0;
    internal const int NativeEnvelopeOverheadBytes = 65_536;
    internal static readonly TimeSpan MembershipRefresh = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);
}
