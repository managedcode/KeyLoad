using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Defines the same resource grants for the eight persisted functional-gate identities.</summary>
internal static class FunctionalGateUserPolicies
{
    private const int Resources = 3;
    private const int FirstResource = 0;
    internal const Capability ReadCapabilities = Capability.DocumentsRead | Capability.Query | Capability.EventsRead | Capability.EventsReplay
        | Capability.QueueInspect | Capability.DeadLettersRead | Capability.GraphRead | Capability.SeriesRead
        | Capability.VectorSearch | Capability.VectorRaw | Capability.TopicsRead | Capability.ChangesRead | Capability.BlobRead
        | Capability.AnalyticsRead;
    internal const Capability WriteCapabilities = ReadCapabilities | Capability.DocumentsWrite | Capability.EventsAppend
        | Capability.QueuePublish | Capability.QueueConsume | Capability.QueueAck | Capability.QueueRenew
        | Capability.DeadLettersRedrive | Capability.SubscriptionsManage | Capability.GraphWrite | Capability.SeriesAppend
        | Capability.TopicsPublish | Capability.SubscriptionsConsume | Capability.SubscriptionsAck | Capability.BlobWrite
        | Capability.BlobDelete | Capability.BlobManage | Capability.SeriesManage
        | Capability.EventsSnapshotsManage | Capability.SchedulerManage | Capability.QueueCancel | Capability.InboxWrite
        | Capability.AnalyticsWrite;
    private static readonly string[] ResourcePrefixes = ["gate_docs_", "gate_sql_", "gate_events_", "gate_queue_",
        "gate_topic_", "gate_blobs_", "gate_graph_", "gate_vectors_", "gate_text_", "gate_series_"];

    internal static ImmutableArray<ScopeGrant> Grants(string database, Capability capabilities, string analyticsOwner)
        => [.. ResourcePrefixes.SelectMany(prefix => Enumerable.Range(FirstResource, Resources)
                .Select(index => new ScopeGrant(database, prefix + index, capabilities))),
            .. Enumerable.Range(FirstResource, Resources).Select(index => new ScopeGrant(database,
                FunctionalGateUserAnalyticsState.StoreName(analyticsOwner, index), capabilities))];

}
