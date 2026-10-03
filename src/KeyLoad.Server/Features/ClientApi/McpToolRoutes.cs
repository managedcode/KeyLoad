namespace KeyLoad.Server;

/// <summary>Existing canonical admission paths for every supported tool.</summary>
internal static class McpToolRoutes
{
    internal const string DocumentsGet = "/v1/documents/get";
    internal const string StreamsRead = "/v1/streams/read";
    internal const string StreamsReplay = AggregateReplayProtocol.Route;
    internal const string EventsRead = "/v1/events/read";
    internal const string SubscriptionsStatus = "/v1/subscriptions/status";
    internal const string MessagesInspect = "/v1/queues/inspect";
    internal const string GraphTraverse = "/v1/graph/traverse";
    internal const string SeriesRead = "/v1/series/read";
    internal const string SeriesLatest = TimeSeriesReadProtocol.LatestRoute;
    internal const string SeriesAggregate = TimeSeriesReadProtocol.AggregateRoute;
    internal const string SeriesWindows = TimeSeriesReadProtocol.WindowsRoute;
    internal const string SeriesRetention = TimeSeriesReadProtocol.RetentionRoute;
    internal const string QueryExecute = "/v1/query";
    internal const string QueryAst = "/v1/query/ast";
    internal const string QueryCapabilities = "/v1/query/capabilities";
    internal const string ChangesRead = "/v1/changes/read";
    internal const string QueryLiveStart = "/v1/query/live/start";
    internal const string QueryLiveRead = "/v1/query/live/read";
    internal const string OutboxStatus = "/v1/admin/outbox/status";
    internal const string ProjectionsRead = "/v1/admin/projections/read";
    internal const string SearchExecute = "/v1/search";
    internal const string AdminBackup = "/v1/admin/backup";
    internal const string AdminAdmission = "/v1/admin/admission";
    internal const string AdminStatus = "/v1/status";
    internal const string DocumentsCommit = "/v1/commands";
    internal const string MessagesReceive = "/v1/queues/receive";
    internal const string MessagesComplete = "/v1/queues/delivery";
    internal const string MessagesProcess = "/v1/queues/process";
    internal const string ResourcesConfigure = "/v1/admin/resources";
    internal const string PrincipalsConfigure = "/v1/admin/principals";
    internal const string CredentialsConfigure = "/v1/admin/api-keys";
    internal const string AdminDispatch = "/v1/admin/dispatch";
    internal const string SubscriptionsConfigure = "/v1/subscriptions/configure";
    internal const string SubscriptionsSeek = "/v1/subscriptions/seek";
    internal const string SubscriptionsReceive = "/v1/subscriptions/receive";
    internal const string SubscriptionsComplete = "/v1/subscriptions/delivery";
    internal const string SubscriptionsProcess = "/v1/subscriptions/process";
    internal const string SubscriptionsPause = "/v1/subscriptions/pause";
    internal const string ProjectionsConfigure = "/v1/admin/projections/configure";
    internal const string ProjectionsCommit = "/v1/admin/projections/commit";
    internal const string ProjectionsRelease = "/v1/admin/projections/release";
    internal const string OutboxPurge = "/v1/admin/outbox/purge";
}
