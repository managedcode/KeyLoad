namespace KeyLoad.Clustering;

internal static class EventFeedPubSubProtocol
{
    internal const string ProviderName = "keyload-event-feed-advisory";
    internal const string StateName = "PubSubRendezvousGrain";
    internal const string GrainTypeName = "pubsubrendezvous";
    internal const string KeySeparator = "/";
    internal const string Invalid = "The execution event feed advisory storage identity is invalid.";
    internal const string Conflict = "The execution event feed advisory storage revision changed.";
}
