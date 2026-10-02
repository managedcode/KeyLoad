namespace KeyLoad.Server;

/// <summary>Stable version-one tool identities frozen by ADR-039.</summary>
internal static class McpToolNames
{
    internal const string DocumentsGet = "keyload_documents_get";
    internal const string StreamsRead = "keyload_streams_read";
    internal const string EventsRead = "keyload_events_read";
    internal const string SubscriptionsStatus = "keyload_subscriptions_status";
    internal const string MessagesInspect = "keyload_messages_inspect";
    internal const string GraphTraverse = "keyload_graph_traverse";
    internal const string SeriesRead = "keyload_series_read";
    internal const string QueryExecute = "keyload_query_execute";
    internal const string QueryAst = "keyload_query_ast";
    internal const string QueryCapabilities = "keyload_query_capabilities";
    internal const string ChangesRead = "keyload_changes_read";
    internal const string QueryLiveStart = "keyload_query_live_start";
    internal const string QueryLiveRead = "keyload_query_live_read";
    internal const string OutboxStatus = "keyload_outbox_status";
    internal const string ProjectionsRead = "keyload_projections_read";
    internal const string SearchExecute = "keyload_search_execute";
    internal const string AdminBackup = "keyload_admin_backup";
    internal const string AdminAdmission = "keyload_admin_admission";
    internal const string AdminStatus = "keyload_admin_status";
    internal const string DocumentsCommit = "keyload_documents_commit";
    internal const string MessagesReceive = "keyload_messages_receive";
    internal const string MessagesComplete = "keyload_messages_complete";
    internal const string MessagesProcess = "keyload_messages_process";
    internal const string ResourcesConfigure = "keyload_resources_configure";
    internal const string PrincipalsConfigure = "keyload_principals_configure";
    internal const string CredentialsConfigure = "keyload_credentials_configure";
    internal const string AdminDispatch = "keyload_admin_dispatch";
    internal const string SubscriptionsConfigure = "keyload_subscriptions_configure";
    internal const string SubscriptionsSeek = "keyload_subscriptions_seek";
    internal const string SubscriptionsReceive = "keyload_subscriptions_receive";
    internal const string SubscriptionsComplete = "keyload_subscriptions_complete";
    internal const string SubscriptionsProcess = "keyload_subscriptions_process";
    internal const string SubscriptionsPause = "keyload_subscriptions_pause";
    internal const string ProjectionsConfigure = "keyload_projections_configure";
    internal const string ProjectionsCommit = "keyload_projections_commit";
    internal const string ProjectionsRelease = "keyload_projections_release";
    internal const string OutboxPurge = "keyload_outbox_purge";
}
