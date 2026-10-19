namespace KeyLoad.Client.Features.ClientApi;

internal static class ClientApiRoutes
{
    internal const string Commands = "/commands";
    internal const string DocumentsGet = "/documents/get";
    internal const string StreamsRead = "/streams/read";
    internal const string EventsRead = "/events/read";
    internal const string SubscriptionsConfigure = "/subscriptions/configure";
    internal const string SubscriptionsSeek = "/subscriptions/seek";
    internal const string SubscriptionsPause = "/subscriptions/pause";
    internal const string SubscriptionsReceive = "/subscriptions/receive";
    internal const string SubscriptionsDelivery = "/subscriptions/delivery";
    internal const string SubscriptionsProcess = "/subscriptions/process";
    internal const string SubscriptionsStatus = "/subscriptions/status";
    internal const string QueuesReceive = "/queues/receive";
    internal const string QueuesDelivery = "/queues/delivery";
    internal const string InboxCommit = "/inbox/commit";
    internal const string QueuesProcess = "/queues/process";
    internal const string QueuesInspect = "/queues/inspect";
    internal const string Query = "/query";
    internal const string QueryAst = "/query/ast";
    internal const string QueryCapabilities = "/query/capabilities";
    internal const string ChangesRead = "/changes/read";
    internal const string LiveQueryStart = "/query/live/start";
    internal const string LiveQueryRead = "/query/live/read";
    internal const string OutboxStatus = "/admin/outbox/status";
    internal const string OutboxPurge = "/admin/outbox/purge";
    internal const string ProjectionsConfigure = "/admin/projections/configure";
    internal const string ProjectionsRead = "/admin/projections/read";
    internal const string ProjectionsCommit = "/admin/projections/commit";
    internal const string ProjectionsRelease = "/admin/projections/release";
    internal const string Search = "/search";
    internal const string GraphTraverse = "/graph/traverse";
    internal const string SeriesRead = "/series/read";
    internal const string TablesDrop = "/admin/tables/drop";
    internal const string ResourcesConfigure = "/admin/resources";
    internal const string PrincipalsConfigure = "/admin/principals";
    internal const string ApiKeysConfigure = "/admin/api-keys";
    internal const string AdminBackup = "/admin/backup";
    internal const string AdminDispatch = "/admin/dispatch";
    internal const string DispatchPausedQuery = "?paused=";
    internal const string Status = "/status";
    internal const string AdmissionStatus = "/admin/admission";
}
