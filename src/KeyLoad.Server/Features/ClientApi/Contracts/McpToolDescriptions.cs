namespace KeyLoad.Server;

/// <summary>Explicit agent guidance for each frozen public capability.</summary>
internal static class McpToolDescriptions
{
    internal const string StableRetry = " Retry a command only with the same stable ID and canonical payload; timeout or disconnect does not prove rollback.";
    private const string DocumentsGet = "Read one authorized document revision; result is null when absent. Optional minimumToken requires the acknowledged position in the same physical incarnation and placement after a fresh quorum barrier. Invalid tokens fail; redaction follows current persisted grants.";
    private const string StreamsRead = "Read a bounded event stream page after a revision; continue using the returned revision and hasMore indicator.";
    private const string StreamsReplay = "Read one exact-versioned aggregate snapshot and its complete bounded event tail under one committed cut. Requires persisted worker capabilities and every raw payload/header grant; incompatible versions or unavailable history fail. Replay invokes no subscription or external effect.";
    private const string EventsRead = "Read a bounded topic or stream page; preserve the returned cursor when continuing.";
    private const string SubscriptionsStatus = "Read current subscription configuration, checkpoint, generation and delivery state.";
    private const string MessagesInspect = "Inspect authorized queue message metadata and visible payload; result is null when absent.";
    private const string QueueTransferInspect = "Read one persisted source transfer and signed intent under current administrator, data and field grants; null when absent.";
    private const string QueueTransferReceipt = "Read the committed signed destination receipt for a transfer; this does not assert source completion. Null when absent.";
    private const string ScheduleInspect = "Read one recurring schedule with current payload and header projection; null when absent.";
    private const string SagaInspect = "Read projected current saga state and deadline; null when absent.";
    private const string QuerySearch = "Execute the bounded Q1.Search.v1 SQL graph-search profile under the same current policy and read cut as graph search.";
    private const string GraphTraverse = "Traverse a graph with explicit depth, vertex and edge limits under the current principal.";
    private const string GraphShortestPath = "Find one shortest directed path between authorized graph entities in one current read cut, with explicit depth, vertex and examined-edge limits.";
    private const string GraphIncomingEdges = "Read a bounded eventual reverse-adjacency page for one authorized graph target; rows are verified against current canonical source edges and endpoint visibility.";
    private const string QueryGraphPath = "Execute the bounded Q1.GraphPath.v1 SQL profile for one shortest directed graph path under the same current policy and read cut as the typed graph operation.";
    private const string SeriesRead = "Read a bounded time-series page in the supplied UTC time range.";
    private const string SeriesLatest = "Read the latest authorized sample at or before an optional inclusive UTC timestamp; sample is null when absent.";
    private const string SeriesAggregate = "Read complete raw count, sum, minimum, maximum and sum/count average in [from, untilExclusive); null end includes the maximum timestamp. Exceeding maxSamples rejects the whole result.";
    private const string SeriesWindows = "Read dense fixed-width UTC windows anchored at from, including empty windows and a clamped final window. Sample and window caps reject the whole result when exceeded.";
    private const string SeriesRollup = "Read one current explicit UTC rollup bucket with exact revision, raw watermark and finite sum/count statistics; stale snapshots reject until explicit correction.";
    private const string SeriesRetention = "Read the persisted exclusive UTC retention floor, cumulative physical purge count and remaining-page status under current series read authorization.";
    private const string QueryExecute = "Execute an authorized read-only query with bounded work; continue with its returned cursor.";
    private const string QueryAst = "Execute the canonical typed query AST. Put polymorphic kind before other object fields and preserve its returned cursor.";
    private const string QueryPartitions = "Read one complete bounded query over up to eight authorized atomic partitions on the same physical owner. Results carry full entity references and separate leaf cuts; this operation has no cursor or global snapshot.";
    private const string QueryCapabilities = "Discover supported query versions, predicates, read profiles and bounded execution limits.";
    private const string ChangesRead = "Read a bounded document change-feed page and preserve the returned signed cursor.";
    private const string QueryLiveStart = "Start a bounded live query snapshot and retain its cursor for subsequent reads.";
    private const string QueryLiveRead = "Read the next bounded live query page using the original query and returned cursor.";
    private const string OutboxStatus = "Read administrator-authorized outbox usage and projection-consumer progress.";
    private const string ProjectionsRead = "Read a bounded administrator-authorized projection batch and retain its acknowledgement token.";
    private const string WaitForIndex = "Publish the authorized native lexical generation at an acknowledged minimum applied cut with bounded cancellation and deadline.";
    private const string SearchExecute = "Search authorized documents with lexical, vector or hybrid ranking and explicit result limits.";
    private const string SearchGraph = "Search with versioned graph scope and shortest-hop retrieval in one authorized read cut; optional bounded context is expanded only from selected hits.";
    private const string AdminBackup = "Create an administrator-authorized physical node backup. Retrying can create another archive; this operation has filesystem side effects.";
    private const string AdminAdmission = "Read administrator-authorized admission limits and actual node usage.";
    private const string AdminStatus = "Read administrator-authorized physical node identity, readiness and replication progress.";
    private const string DocumentsCommit = "Commit one authorized atomic batch of supported canonical mutations. Put each mutation kind before other object fields.";
    private const string MessagesReceiveAcrossLanes = "Receive ordered independent queue lane outcomes. Retain each original request.requests receive requestId and payload for retry; there is no group transaction, receipt or shared read cut. Unknown stops later dispatch; cancellation may leave committed leases.";
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
    private const string AdminPartitionPlacementBind = "Bind one atomic partition to the current physical shard using the expected placement directory revision; requires persisted cluster administrator authority.";
    private const string AdminPartitionPlacementRead = "Read one atomic partition placement and the committed default-shard identity from one authorized view; requires persisted cluster administrator authority.";

    internal static string For(string name) => name switch
    {
        McpToolNames.DocumentsGet => DocumentsGet,
        McpToolNames.StreamsRead => StreamsRead,
        McpToolNames.StreamsReplay => StreamsReplay,
        McpToolNames.EventsRead => EventsRead,
        McpToolNames.SubscriptionsStatus => SubscriptionsStatus,
        McpToolNames.MessagesInspect => MessagesInspect,
        McpToolNames.QueueTransferInspect => QueueTransferInspect,
        McpToolNames.QueueTransferReceipt => QueueTransferReceipt,
        McpToolNames.ScheduleInspect => ScheduleInspect,
        McpToolNames.SagaInspect => SagaInspect,
        McpToolNames.GraphTraverse => GraphTraverse,
        McpToolNames.GraphShortestPath => GraphShortestPath,
        McpToolNames.GraphIncomingEdges => GraphIncomingEdges,
        McpToolNames.SeriesRead or McpToolNames.SeriesLatest or McpToolNames.SeriesAggregate
            or McpToolNames.SeriesWindows or McpToolNames.SeriesRetention or McpToolNames.SeriesRollup => SeriesDescription(name),
        McpToolNames.QuerySearch or McpToolNames.QueryGraphPath or McpToolNames.QueryExecute or McpToolNames.QueryAst
            or McpToolNames.QueryCapabilities or McpToolNames.QueryLiveStart or McpToolNames.QueryLiveRead or McpToolNames.QueryPartitions
            => QueryDescription(name),
        McpToolNames.ChangesRead => ChangesRead,
        McpToolNames.OutboxStatus => OutboxStatus,
        McpToolNames.ProjectionsRead => ProjectionsRead,
        WaitForIndexProtocol.Tool => WaitForIndex,
        McpToolNames.SearchExecute => SearchExecute,
        McpToolNames.SearchGraph => SearchGraph,
        McpToolNames.AdminBackup or McpToolNames.AdminAdmission or McpToolNames.AdminStatus
            or McpToolNames.AdminPartitionPlacementBind or McpToolNames.AdminPartitionPlacementRead
            => AdminDescription(name),
        McpToolNames.DocumentsCommit => DocumentsCommit,
        McpToolNames.SearchAnnMaintain => AnnMaintenanceProtocol.Description,
        McpToolNames.MessagesReceiveAcrossLanes => MessagesReceiveAcrossLanes,
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

    private static string AdminDescription(string name) => name switch
    {
        McpToolNames.AdminBackup => AdminBackup,
        McpToolNames.AdminAdmission => AdminAdmission,
        McpToolNames.AdminStatus => AdminStatus,
        McpToolNames.AdminPartitionPlacementBind => AdminPartitionPlacementBind,
        McpToolNames.AdminPartitionPlacementRead => AdminPartitionPlacementRead,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static string SeriesDescription(string name) => name switch
    {
        McpToolNames.SeriesRead => SeriesRead,
        McpToolNames.SeriesLatest => SeriesLatest,
        McpToolNames.SeriesAggregate => SeriesAggregate,
        McpToolNames.SeriesWindows => SeriesWindows,
        McpToolNames.SeriesRetention => SeriesRetention,
        McpToolNames.SeriesRollup => SeriesRollup,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static string QueryDescription(string name) => name switch
    {
        McpToolNames.QuerySearch => QuerySearch,
        McpToolNames.QueryGraphPath => QueryGraphPath,
        McpToolNames.QueryExecute => QueryExecute,
        McpToolNames.QueryAst => QueryAst,
        McpToolNames.QueryPartitions => QueryPartitions,
        McpToolNames.QueryCapabilities => QueryCapabilities,
        McpToolNames.QueryLiveStart => QueryLiveStart,
        McpToolNames.QueryLiveRead => QueryLiveRead,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };
}
