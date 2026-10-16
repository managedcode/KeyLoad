using ZoneTree.Analytics;

namespace KeyLoad.Core.Features.AnalyticsStore;

/// <summary>Bounds one disposable derived generation; canonical source rows own retention and acknowledgements.</summary>
internal static class AnalyticsStoreNativeOptions
{
    internal const string Site = "keyload-analytics-store";
    internal const string Event = "canonical-event";
    private const int SingleWorker = 1;

    internal static AnalyticsOptions Create(string ownedDirectory, DatabaseLimits limits,
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
        options.Storage.RawEvents.MutableSegmentMaxItemCount = limits.MaxScanRecords;
        options.Storage.RawEvents.DiskSegmentMaxItemCount = limits.MaxScanRecords;
        options.Storage.DimensionIndexes.Dimensions.Clear();
        options.Storage.RollupIndexes.Definitions.Clear();
        options.Rollups.Granularities = Granularity.None;
        options.Rollups.Definitions.Clear();
        options.Sessions.Enabled = false;
        options.Sessions.MaxCachedSessions = SingleWorker;
        options.Privacy.VisitorIdentification = VisitorIdentification.None;
        options.Privacy.GeoPrecision = GeoPrecision.None;
        options.Privacy.CollectUserAgentDetails = false;
        options.Privacy.CollectVersions = false;
        options.Privacy.IncludeQueryString = false;
        options.Privacy.AllowedDimensions = new HashSet<string>(dimensions, StringComparer.Ordinal);
        options.Privacy.MaxCustomDimensions = dimensions.Count;
        options.Privacy.MaxCustomValues = metricCount;
        options.Privacy.MaxValueLength = limits.MaxBatchBytes;
        options.Web.GeoLocator = null;
        options.Web.UserAgentParser = null;
        options.Web.BotDetectors.Clear();
        options.Ingestion.WorkerCount = SingleWorker;
        options.Ingestion.QueueCapacity = limits.MaxScanRecords;
        options.Ingestion.MaxBufferedRollupRows = limits.MaxScanRecords;
        options.Ingestion.QueueFullBehavior = QueueFullBehavior.Wait;
        options.Retention.RawEvents = null;
        options.Queries.Timeout = TimeSpan.FromSeconds(limits.QueryDeadlineSeconds);
        options.Queries.MaxDegreeOfParallelism = SingleWorker;
        options.Queries.MaxGroupBy = Math.Max(SingleWorker, dimensions.Count);
        options.Queries.MaxGroups = limits.MaxResults;
        options.Queries.MaxTimeBuckets = limits.MaxResults;
        options.Queries.MaxFilledRows = limits.MaxResults;
        return options;
    }
}
