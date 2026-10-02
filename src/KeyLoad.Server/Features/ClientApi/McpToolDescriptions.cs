namespace KeyLoad.Server;

/// <summary>Explicit agent guidance for each frozen public capability.</summary>
internal static class McpToolDescriptions
{
    internal const string StableRetry = " Retry a command only with the same stable ID and canonical payload; timeout or disconnect does not prove rollback.";
    private const string DocumentsGet = "Read one authorized document revision; result is null when absent. Redaction follows current persisted grants.";
    private const string StreamsRead = "Read a bounded event stream page after a revision; continue using the returned revision and hasMore indicator.";
    private const string EventsRead = "Read a bounded topic or stream page; preserve the returned cursor when continuing.";
    private const string SubscriptionsStatus = "Read current subscription configuration, checkpoint, generation and delivery state.";
    private const string MessagesInspect = "Inspect authorized queue message metadata and visible payload; result is null when absent.";
    private const string GraphTraverse = "Traverse a graph with explicit depth, vertex and edge limits under the current principal.";
    private const string SeriesRead = "Read a bounded time-series page in the supplied UTC time range.";
    private const string QueryExecute = "Execute an authorized read-only query with bounded work; continue with its returned cursor.";
    private const string QueryAst = "Execute the canonical typed query AST. Put polymorphic kind before other object fields and preserve its returned cursor.";
    private const string QueryCapabilities = "Discover supported query versions, predicates, read profiles and bounded execution limits.";
    private const string ChangesRead = "Read a bounded document change-feed page and preserve the returned signed cursor.";
    private const string QueryLiveStart = "Start a bounded live query snapshot and retain its cursor for subsequent reads.";
    private const string QueryLiveRead = "Read the next bounded live query page using the original query and returned cursor.";
    private const string OutboxStatus = "Read administrator-authorized outbox usage and projection-consumer progress.";
    private const string ProjectionsRead = "Read a bounded administrator-authorized projection batch and retain its acknowledgement token.";
    private const string SearchExecute = "Search authorized documents with lexical, vector or hybrid ranking and explicit result limits.";
    private const string AdminBackup = "Create an administrator-authorized physical node backup. Retrying can create another archive; this operation has filesystem side effects.";
    private const string AdminAdmission = "Read administrator-authorized admission limits and actual node usage.";
    private const string AdminStatus = "Read administrator-authorized physical node identity, readiness and replication progress.";
    private const string DocumentsCommit = "Commit one atomic batch containing any of the ten canonical mutation variants. Put each mutation kind before other object fields.";
    private const string MessagesReceive = "Receive a bounded set of queue messages. request.requestId is the stable write identity; retain delivery tokens for completion.";
    private const string MessagesComplete = "Acknowledge, reject or renew a queue delivery using its signed token.";
    private const string MessagesProcess = "Atomically complete an idempotent queue handler and its declared mutation effects.";
    private const string ResourcesConfigure = "Configure a resource as an administrator. Supply a stable outer commandId together with the canonical request.";
    private const string PrincipalsConfigure = "Configure a persisted principal as an administrator. Supply a stable outer commandId; this request never establishes caller authority.";
    private const string CredentialsConfigure = "Configure a persisted API-key verifier as an administrator. Supply a stable outer commandId; do not send a plaintext key.";
    private const string AdminDispatch = "Pause or resume database dispatch as an administrator. request is a boolean and commandId is the stable outer write identity.";
    private const string SubscriptionsConfigure = "Configure a durable subscription and its starting position under current persisted grants.";
    private const string SubscriptionsSeek = "Seek a durable subscription with its expected generation and explicit starting policy.";
    private const string SubscriptionsReceive = "Receive a bounded subscription batch. request.requestId is the stable write identity; retain its delivery tokens.";
    private const string SubscriptionsComplete = "Acknowledge, reject or renew a subscription delivery using its signed token.";
    private const string SubscriptionsProcess = "Atomically complete a subscription handler and its declared mutation effects.";
    private const string SubscriptionsPause = "Pause or resume a durable subscription using its expected generation.";
    private const string ProjectionsConfigure = "Configure an administrator-authorized projection consumer and its starting cut.";
    private const string ProjectionsCommit = "Commit a projection batch acknowledgement and declared mutation effects with the original token.";
    private const string ProjectionsRelease = "Release an administrator-authorized projection consumer at its expected index generation.";
    private const string OutboxPurge = "Purge a bounded administrator-authorized outbox range. Existing retained data can be removed.";

    internal static string For(string name) => name switch
    {
        McpToolNames.DocumentsGet => DocumentsGet,
        McpToolNames.StreamsRead => StreamsRead,
        McpToolNames.EventsRead => EventsRead,
        McpToolNames.SubscriptionsStatus => SubscriptionsStatus,
        McpToolNames.MessagesInspect => MessagesInspect,
        McpToolNames.GraphTraverse => GraphTraverse,
        McpToolNames.SeriesRead => SeriesRead,
        McpToolNames.QueryExecute => QueryExecute,
        McpToolNames.QueryAst => QueryAst,
        McpToolNames.QueryCapabilities => QueryCapabilities,
        McpToolNames.ChangesRead => ChangesRead,
        McpToolNames.QueryLiveStart => QueryLiveStart,
        McpToolNames.QueryLiveRead => QueryLiveRead,
        McpToolNames.OutboxStatus => OutboxStatus,
        McpToolNames.ProjectionsRead => ProjectionsRead,
        McpToolNames.SearchExecute => SearchExecute,
        McpToolNames.AdminBackup => AdminBackup,
        McpToolNames.AdminAdmission => AdminAdmission,
        McpToolNames.AdminStatus => AdminStatus,
        McpToolNames.DocumentsCommit => DocumentsCommit,
        McpToolNames.MessagesReceive => MessagesReceive,
        McpToolNames.MessagesComplete => MessagesComplete,
        McpToolNames.MessagesProcess => MessagesProcess,
        McpToolNames.ResourcesConfigure => ResourcesConfigure,
        McpToolNames.PrincipalsConfigure => PrincipalsConfigure,
        McpToolNames.CredentialsConfigure => CredentialsConfigure,
        McpToolNames.AdminDispatch => AdminDispatch,
        McpToolNames.SubscriptionsConfigure => SubscriptionsConfigure,
        McpToolNames.SubscriptionsSeek => SubscriptionsSeek,
        McpToolNames.SubscriptionsReceive => SubscriptionsReceive,
        McpToolNames.SubscriptionsComplete => SubscriptionsComplete,
        McpToolNames.SubscriptionsProcess => SubscriptionsProcess,
        McpToolNames.SubscriptionsPause => SubscriptionsPause,
        McpToolNames.ProjectionsConfigure => ProjectionsConfigure,
        McpToolNames.ProjectionsCommit => ProjectionsCommit,
        McpToolNames.ProjectionsRelease => ProjectionsRelease,
        McpToolNames.OutboxPurge => OutboxPurge,
        AdminDashboardProtocol.SnapshotTool or AdminDashboardProtocol.ResourcesTool or AdminDashboardProtocol.QueueTool
            => AdminDashboardMcpCatalog.Description(name),
        _ => BlobMcpDescriptions.For(name)
    };
}
