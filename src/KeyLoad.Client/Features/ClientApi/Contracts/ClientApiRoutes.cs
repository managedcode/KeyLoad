namespace KeyLoad.Client.Features.ClientApi;

internal static class ClientApiRoutes
{
    internal const string Commands = "/v1/commands";
    internal const string DocumentsGet = "/v1/documents/get";
    internal const string StreamsRead = "/v1/streams/read";
    internal const string EventsRead = "/v1/events/read";
    internal const string SubscriptionsConfigure = "/v1/subscriptions/configure";
    internal const string SubscriptionsSeek = "/v1/subscriptions/seek";
    internal const string SubscriptionsPause = "/v1/subscriptions/pause";
    internal const string SubscriptionsReceive = "/v1/subscriptions/receive";
    internal const string SubscriptionsDelivery = "/v1/subscriptions/delivery";
    internal const string SubscriptionsProcess = "/v1/subscriptions/process";
    internal const string SubscriptionsStatus = "/v1/subscriptions/status";
    internal const string QueuesReceive = "/v1/queues/receive";
    internal const string QueuesDelivery = "/v1/queues/delivery";
    internal const string QueuesProcess = "/v1/queues/process";
    internal const string QueuesInspect = "/v1/queues/inspect";
    internal const string Query = "/v1/query";
    internal const string QueryAst = "/v1/query/ast";
    internal const string QueryCapabilities = "/v1/query/capabilities";
    internal const string ChangesRead = "/v1/changes/read";
    internal const string LiveQueryStart = "/v1/query/live/start";
    internal const string LiveQueryRead = "/v1/query/live/read";
    internal const string OutboxStatus = "/v1/admin/outbox/status";
    internal const string OutboxPurge = "/v1/admin/outbox/purge";
    internal const string ProjectionsConfigure = "/v1/admin/projections/configure";
    internal const string ProjectionsRead = "/v1/admin/projections/read";
    internal const string ProjectionsCommit = "/v1/admin/projections/commit";
    internal const string ProjectionsRelease = "/v1/admin/projections/release";
    internal const string Search = "/v1/search";
    internal const string GraphTraverse = "/v1/graph/traverse";
    internal const string SeriesRead = "/v1/series/read";
    internal const string ResourcesConfigure = "/v1/admin/resources";
    internal const string PrincipalsConfigure = "/v1/admin/principals";
    internal const string ApiKeysConfigure = "/v1/admin/api-keys";
    internal const string AdminBackup = "/v1/admin/backup";
    internal const string AdminDispatch = "/v1/admin/dispatch";
    internal const string DispatchPausedQuery = "?paused=";
    internal const string Status = "/v1/status";
    internal const string AdmissionStatus = "/v1/admin/admission";
}
