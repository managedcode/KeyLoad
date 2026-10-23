namespace KeyLoad;

/// <summary>The one canonical set of public HTTP route paths shared by the server, the SDK and the MCP catalog.</summary>
public static class ApiRoutes
{
    /// <summary>The "/commands" route.</summary>
    public const string Commands = "/commands";
    /// <summary>The "/documents/get" route.</summary>
    public const string DocumentsGet = "/documents/get";
    /// <summary>The "/streams/read" route.</summary>
    public const string StreamsRead = "/streams/read";
    /// <summary>The "/events/read" route.</summary>
    public const string EventsRead = "/events/read";
    /// <summary>The "/subscriptions/configure" route.</summary>
    public const string SubscriptionsConfigure = "/subscriptions/configure";
    /// <summary>The "/subscriptions/seek" route.</summary>
    public const string SubscriptionsSeek = "/subscriptions/seek";
    /// <summary>The "/subscriptions/pause" route.</summary>
    public const string SubscriptionsPause = "/subscriptions/pause";
    /// <summary>The "/subscriptions/receive" route.</summary>
    public const string SubscriptionsReceive = "/subscriptions/receive";
    /// <summary>The "/subscriptions/delivery" route.</summary>
    public const string SubscriptionsDelivery = "/subscriptions/delivery";
    /// <summary>The "/subscriptions/process" route.</summary>
    public const string SubscriptionsProcess = "/subscriptions/process";
    /// <summary>The "/subscriptions/status" route.</summary>
    public const string SubscriptionsStatus = "/subscriptions/status";
    /// <summary>The "/queues/receive" route.</summary>
    public const string QueuesReceive = "/queues/receive";
    /// <summary>The "/queues/delivery" route.</summary>
    public const string QueuesDelivery = "/queues/delivery";
    /// <summary>The "/inbox/commit" route.</summary>
    public const string InboxCommit = "/inbox/commit";
    /// <summary>The "/queues/process" route.</summary>
    public const string QueuesProcess = "/queues/process";
    /// <summary>The "/queues/inspect" route.</summary>
    public const string QueuesInspect = "/queues/inspect";
    /// <summary>The "/query" route.</summary>
    public const string Query = "/query";
    /// <summary>The "/query/ast" route.</summary>
    public const string QueryAst = "/query/ast";
    /// <summary>The "/query/capabilities" route.</summary>
    public const string QueryCapabilities = "/query/capabilities";
    /// <summary>The "/changes/read" route.</summary>
    public const string ChangesRead = "/changes/read";
    /// <summary>The "/query/live/start" route.</summary>
    public const string LiveQueryStart = "/query/live/start";
    /// <summary>The "/query/live/read" route.</summary>
    public const string LiveQueryRead = "/query/live/read";
    /// <summary>The "/admin/outbox/status" route.</summary>
    public const string OutboxStatus = "/admin/outbox/status";
    /// <summary>The "/admin/outbox/purge" route.</summary>
    public const string OutboxPurge = "/admin/outbox/purge";
    /// <summary>The "/admin/projections/configure" route.</summary>
    public const string ProjectionsConfigure = "/admin/projections/configure";
    /// <summary>The "/admin/projections/read" route.</summary>
    public const string ProjectionsRead = "/admin/projections/read";
    /// <summary>The "/admin/projections/commit" route.</summary>
    public const string ProjectionsCommit = "/admin/projections/commit";
    /// <summary>The "/admin/projections/release" route.</summary>
    public const string ProjectionsRelease = "/admin/projections/release";
    /// <summary>The "/search" route.</summary>
    public const string Search = "/search";
    /// <summary>The "/graph/traverse" route.</summary>
    public const string GraphTraverse = "/graph/traverse";
    /// <summary>The "/series/read" route.</summary>
    public const string SeriesRead = "/series/read";
    /// <summary>The "/admin/tables/drop" route.</summary>
    public const string TablesDrop = "/admin/tables/drop";
    /// <summary>The "/admin/resources" route.</summary>
    public const string ResourcesConfigure = "/admin/resources";
    /// <summary>The "/admin/principals" route.</summary>
    public const string PrincipalsConfigure = "/admin/principals";
    /// <summary>The "/admin/api-keys" route.</summary>
    public const string ApiKeysConfigure = "/admin/api-keys";
    /// <summary>The "/admin/backup" route.</summary>
    public const string AdminBackup = "/admin/backup";
    /// <summary>The "/admin/dispatch" route.</summary>
    public const string AdminDispatch = "/admin/dispatch";
    /// <summary>The "/status" route.</summary>
    public const string Status = "/status";
    /// <summary>The "/admin/admission" route.</summary>
    public const string AdmissionStatus = "/admin/admission";
    /// <summary>The query-string prefix that selects the paused dispatch state.</summary>
    public const string DispatchPausedQuery = "?paused=";
}
