using ZoneTree.Analytics;

namespace KeyLoad.Core.AnalyticsStore;

/// <summary>Bounds one disposable derived generation; canonical source rows own retention and acknowledgements.</summary>
internal static class AnalyticsStoreOptions
{
    internal const string Site = "keyload-analytics-store";
    internal const string Event = "canonical-event";
    private const int MinimumQueuedEvents = 1;
    private const int UntruncatedValueLength = int.MaxValue;
    private const int UnboundedQueryRows = int.MaxValue;

    /// <summary>Maps the operation's remaining lifetime to the engine timeout; only an explicit expiry bounds a query.</summary>
    internal static TimeSpan? QueryTimeout(TimeSpan remaining) => remaining == Timeout.InfiniteTimeSpan ? null : remaining;

    internal static AnalyticsOptions Create(string ownedDirectory, int rowCount,
        IReadOnlyCollection<string> dimensions, int metricCount, ZoneTree.Logger.ILogger failures)
    {
        var options = new AnalyticsOptions
        {
            DataDirectory = ownedDirectory,
            DefaultSite = Site,
            TimeZone = TimeZoneInfo.Utc
        };
        options.Storage.Logger = failures;
        options.Storage.StoreRawEvents = true;
        options.Storage.DimensionIndexes.Dimensions.Clear();
        options.Storage.RollupIndexes.Definitions.Clear();
        options.Rollups.Granularities = Granularity.None;
        options.Rollups.Definitions.Clear();
        options.Sessions.Enabled = false;
        options.Sessions.MaxCachedSessions = 1;
        options.Privacy.VisitorIdentification = VisitorIdentification.None;
        options.Privacy.GeoPrecision = GeoPrecision.None;
        options.Privacy.CollectUserAgentDetails = false;
        options.Privacy.CollectVersions = false;
        options.Privacy.IncludeQueryString = false;
        options.Privacy.AllowedDimensions = new HashSet<string>(dimensions, StringComparer.Ordinal);
        options.Privacy.MaxCustomDimensions = dimensions.Count;
        options.Privacy.MaxCustomValues = metricCount;
        options.Privacy.MaxValueLength = UntruncatedValueLength;
        options.Web.GeoLocator = null;
        options.Web.UserAgentParser = null;
        options.Web.BotDetectors.Clear();
        options.Ingestion.WorkerCount = 1;
        options.Ingestion.QueueCapacity = Math.Max(MinimumQueuedEvents, rowCount);
        options.Ingestion.MaxBufferedRollupRows = Math.Max(MinimumQueuedEvents, rowCount);
        options.Ingestion.QueueFullBehavior = QueueFullBehavior.Drop;
        options.Retention.RawEvents = null;
        options.Queries.Timeout = null;
        options.Queries.MaxDegreeOfParallelism = 1;
        options.Queries.MaxGroupBy = Math.Max(1, dimensions.Count);
        options.Queries.MaxTimeBuckets = UnboundedQueryRows;
        options.Queries.MaxFilledRows = UnboundedQueryRows;
        return options;
    }
}
