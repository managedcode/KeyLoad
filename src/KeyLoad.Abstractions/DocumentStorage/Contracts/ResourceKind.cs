namespace KeyLoad;

/// <summary>Identifies the kind of resource configured in a database.</summary>
public enum ResourceKind
{
    /// <summary>A document collection.</summary>
    Collection,
    /// <summary>An event stream set.</summary>
    StreamSet,
    /// <summary>A work queue.</summary>
    WorkQueue,
    /// <summary>A publish-subscribe topic.</summary>
    Topic,
    /// <summary>A graph resource.</summary>
    Graph,
    /// <summary>A time-series resource.</summary>
    TimeSeries,
    /// <summary>A canonical chunked binary object resource.</summary>
    BlobStore,
    /// <summary>An independent canonical user analytics event store.</summary>
    AnalyticsStore = 7
}
