namespace KeyLoad.Orleans;

internal static class EventFeedPubSubProtocol
{
    internal const string ProviderName = "keyload-event-feed-advisory-v1";
    internal const string StateName = "PubSubRendezvousGrain";
    internal const string GrainTypeName = "pubsubrendezvous";
    internal const string KeySeparator = "/";
    internal const string Invalid = "The native event feed advisory storage identity is invalid.";
    internal const string Conflict = "The native event feed advisory storage revision changed.";
    internal const string Capacity = "The native event feed advisory storage bound was exceeded.";
}
