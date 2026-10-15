using ZoneTree.Analytics;

namespace KeyLoad.Server;

internal static class OperationalAnalyticsNativeConfiguration
{
    internal static AnalyticsOptions Create(string nodeDirectory, OperationalAnalyticsOptions settings,
        OperationalAnalyticsNativeLogger logger)
    {
        var options = new AnalyticsOptions
        {
            DataDirectory = Path.Combine(Path.GetFullPath(nodeDirectory), OperationalAnalyticsProtocol.DirectoryName),
            DefaultSite = OperationalAnalyticsProtocol.Site,
            TimeZone = TimeZoneInfo.Utc
        };
        options.Storage.Logger = logger;
        options.Storage.StoreRawEvents = false;
        options.Sessions.Enabled = false;
        options.Sessions.MaxCachedSessions = OperationalAnalyticsProtocol.WorkerCount;
        options.Privacy.VisitorIdentification = VisitorIdentification.None;
        options.Privacy.GeoPrecision = GeoPrecision.None;
        options.Privacy.CollectUserAgentDetails = false;
        options.Privacy.CollectVersions = false;
        options.Privacy.IncludeQueryString = false;
        options.Privacy.AllowedDimensions = new HashSet<string>(StringComparer.Ordinal);
        options.Privacy.MaxCustomDimensions = OperationalAnalyticsProtocol.DimensionCount;
        options.Privacy.MaxCustomValues = OperationalAnalyticsProtocol.ValueCount;
        options.Privacy.MaxValueLength = OperationalAnalyticsProtocol.MinimumValueCharacters;
        options.Web.GeoLocator = null;
        options.Web.UserAgentParser = null;
        options.Web.BotDetectors.Clear();
        options.Rollups.Granularities = Granularity.Hour;
        options.Rollups.Definitions.Clear();
        options.Rollups.Definitions.Add(new RollupDefinition());
        options.Storage.DimensionIndexes.Dimensions.Clear();
        options.Storage.RollupIndexes.Definitions.Clear();
        options.Storage.Rollups.MutableSegmentMaxItemCount = settings.StoreMutableItems;
        options.Storage.Rollups.DiskSegmentMaxItemCount = settings.StoreDiskItems;
        options.Storage.Partitioning.HourRollups = settings.StoreDiskItems;
        options.Ingestion.WorkerCount = OperationalAnalyticsProtocol.WorkerCount;
        options.Ingestion.QueueCapacity = settings.QueueCapacity;
        options.Ingestion.MaxBufferedRollupRows = settings.BufferedRows;
        options.Ingestion.FlushInterval = settings.FlushInterval;
        options.Ingestion.QueueFullBehavior = QueueFullBehavior.Drop;
        options.Retention.HourRollups = settings.Retention;
        options.Retention.CheckInterval = settings.RetentionCheckInterval;
        options.Queries.Timeout = settings.QueryTimeout;
        options.Queries.MaxDegreeOfParallelism = OperationalAnalyticsProtocol.WorkerCount;
        options.Queries.MaxGroupBy = OperationalAnalyticsProtocol.WorkerCount;
        options.Queries.MaxGroups = settings.MaximumGroups;
        options.Queries.MaxTimeBuckets = settings.MaximumBuckets;
        options.Queries.MaxFilledRows = settings.MaximumRows;
        return options;
    }
}
