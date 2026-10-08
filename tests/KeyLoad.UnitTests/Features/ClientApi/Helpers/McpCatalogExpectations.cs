using System.Collections.Immutable;
using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Independent frozen ADR-039 inventory used by AC-MCP-001 contract assertions.</summary>
internal static class McpCatalogExpectations
{
    internal const string DocumentsGet = "keyload_documents_get";
    private const string DocumentsGetRoute = "/v1/documents/get";
    internal const string StreamsRead = "keyload_streams_read";
    private const string StreamsReadRoute = "/v1/streams/read";
    internal const string StreamsReplay = "keyload_streams_replay";
    private const string StreamsReplayRoute = "/v1/streams/replay";
    internal const string EventsRead = "keyload_events_read";
    private const string EventsReadRoute = "/v1/events/read";
    internal const string SubscriptionsStatus = "keyload_subscriptions_status";
    private const string SubscriptionsStatusRoute = "/v1/subscriptions/status";
    internal const string MessagesInspect = "keyload_messages_inspect";
    private const string MessagesInspectRoute = "/v1/queues/inspect";
    internal const string QueueTransferInspect = "keyload_queue_transfer_inspect";
    private const string QueueTransferInspectRoute = "/v1/queues/transfers/inspect";
    internal const string QueueTransferReceipt = "keyload_queue_transfer_receipt";
    private const string QueueTransferReceiptRoute = "/v1/queues/transfers/receipt";
    internal const string ScheduleInspect = "keyload_schedule_inspect";
    private const string ScheduleInspectRoute = "/v1/queues/schedules/inspect";
    internal const string SagaInspect = "keyload_saga_inspect";
    private const string SagaInspectRoute = "/v1/queues/sagas/inspect";
    internal const string GraphTraverse = "keyload_graph_traverse";
    private const string GraphTraverseRoute = "/v1/graph/traverse";
    internal const string GraphShortestPath = "keyload_graph_shortest_path";
    internal const string GraphIncomingEdges = "keyload_graph_incoming";
    private const string GraphShortestPathRoute = "/v1/graph/shortest-path";
    private const string GraphIncomingEdgesRoute = "/v1/graph/incoming";
    internal const string QueryGraphPath = "keyload_query_graph_path";
    private const string QueryGraphPathRoute = "/v1/query/graph-path";
    internal const string AdminPartitionPlacementBind = "keyload_admin_partition_placement_bind";
    private const string AdminPartitionPlacementBindRoute = "/v1/admin/partition-placement/bind";
    internal const string AdminPartitionPlacementRead = "keyload_admin_partition_placement_read";
    private const string AdminPartitionPlacementReadRoute = "/v1/admin/partition-placement/read";
    internal const string SeriesRead = "keyload_series_read";
    private const string SeriesReadRoute = "/v1/series/read";
    internal const string SeriesLatest = "keyload_series_latest";
    private const string SeriesLatestRoute = "/v1/series/latest";
    internal const string SeriesAggregate = "keyload_series_aggregate";
    private const string SeriesAggregateRoute = "/v1/series/aggregate";
    internal const string SeriesWindows = "keyload_series_windows";
    private const string SeriesWindowsRoute = "/v1/series/windows";
    internal const string SeriesRollup = "keyload_series_read_rollup";
    private const string SeriesRollupRoute = "/v1/series/rollups/read";
    internal const string SeriesRetention = "keyload_series_retention";
    private const string SeriesRetentionRoute = "/v1/series/retention";
    internal const string QueryExecute = "keyload_query_execute";
    private const string QueryExecuteRoute = "/v1/query";
    internal const string QuerySearch = "keyload_query_search";
    private const string QuerySearchRoute = "/v1/query/search";
    internal const string QueryAst = "keyload_query_ast";
    internal const string QueryPartitions = KeyLoad.UnitTests.Features.QueryExecution.PartitionQueryMcpProtocol.Tool;
    private const string QueryPartitionsRoute = KeyLoad.UnitTests.Features.QueryExecution.PartitionQueryMcpProtocol.Route;
    private const string QueryAstRoute = "/v1/query/ast";
    internal const string QueryCapabilities = "keyload_query_capabilities";
    private const string QueryCapabilitiesRoute = "/v1/query/capabilities";
    internal const string ChangesRead = "keyload_changes_read";
    private const string ChangesReadRoute = "/v1/changes/read";
    internal const string QueryLiveStart = "keyload_query_live_start";
    private const string QueryLiveStartRoute = "/v1/query/live/start";
    internal const string QueryLiveRead = "keyload_query_live_read";
    private const string QueryLiveReadRoute = "/v1/query/live/read";
    internal const string OutboxStatus = "keyload_outbox_status";
    private const string OutboxStatusRoute = "/v1/admin/outbox/status";
    internal const string ProjectionsRead = "keyload_projections_read";
    private const string ProjectionsReadRoute = "/v1/admin/projections/read";
    internal const string SearchExecute = "keyload_search_execute";
    private const string SearchExecuteRoute = "/v1/search";
    internal const string SearchGraph = "keyload_search_graph";
    private const string SearchGraphRoute = "/v1/search/graph";
    internal const string AdminBackup = "keyload_admin_backup";
    private const string AdminBackupRoute = "/v1/admin/backup";
    internal const string AdminAdmission = "keyload_admin_admission";
    private const string AdminAdmissionRoute = "/v1/admin/admission";
    internal const string AdminStatus = "keyload_admin_status";
    private const string AdminStatusRoute = "/v1/status";
    internal const string DocumentsCommit = "keyload_documents_commit";
    private const string DocumentsCommitRoute = "/v1/commands";
    internal const string MessagesReceive = "keyload_messages_receive";
    private const string MessagesReceiveRoute = "/v1/queues/receive";
    internal const string MessagesComplete = "keyload_messages_complete";
    private const string MessagesCompleteRoute = "/v1/queues/delivery";
    internal const string MessagesProcess = "keyload_messages_process";
    private const string MessagesProcessRoute = "/v1/queues/process";
    internal const string ResourcesConfigure = "keyload_resources_configure";
    private const string ResourcesConfigureRoute = "/v1/admin/resources";
    internal const string PrincipalsConfigure = "keyload_principals_configure";
    private const string PrincipalsConfigureRoute = "/v1/admin/principals";
    internal const string CredentialsConfigure = "keyload_credentials_configure";
    private const string CredentialsConfigureRoute = "/v1/admin/api-keys";
    internal const string AdminDispatch = "keyload_admin_dispatch";
    private const string AdminDispatchRoute = "/v1/admin/dispatch";
    internal const string SubscriptionsConfigure = "keyload_subscriptions_configure";
    private const string SubscriptionsConfigureRoute = "/v1/subscriptions/configure";
    internal const string SubscriptionsSeek = "keyload_subscriptions_seek";
    private const string SubscriptionsSeekRoute = "/v1/subscriptions/seek";
    internal const string SubscriptionsReceive = "keyload_subscriptions_receive";
    private const string SubscriptionsReceiveRoute = "/v1/subscriptions/receive";
    internal const string SubscriptionsComplete = "keyload_subscriptions_complete";
    private const string SubscriptionsCompleteRoute = "/v1/subscriptions/delivery";
    internal const string SubscriptionsProcess = "keyload_subscriptions_process";
    private const string SubscriptionsProcessRoute = "/v1/subscriptions/process";
    internal const string SubscriptionsPause = "keyload_subscriptions_pause";
    private const string SubscriptionsPauseRoute = "/v1/subscriptions/pause";
    internal const string ProjectionsConfigure = "keyload_projections_configure";
    private const string ProjectionsConfigureRoute = "/v1/admin/projections/configure";
    internal const string ProjectionsCommit = "keyload_projections_commit";
    private const string ProjectionsCommitRoute = "/v1/admin/projections/commit";
    internal const string ProjectionsRelease = "keyload_projections_release";
    private const string ProjectionsReleaseRoute = "/v1/admin/projections/release";
    internal const string OutboxPurge = "keyload_outbox_purge";
    private const string OutboxPurgeRoute = "/v1/admin/outbox/purge";

    private const string DashboardName = "keyload_admin_dashboard";
    private const string DashboardRoute = "/v1/admin/dashboard";
    private const string ResourcesListName = "keyload_admin_resources_list";
    private const string ResourcesListRoute = "/v1/admin/dashboard/resources";
    private const string QueueBrowseName = "keyload_admin_queue_browse";
    private const string QueueBrowseRoute = "/v1/admin/dashboard/queue";
    private const string AnnMaintain = "keyload_search_ann_maintain";
    private const string AnnMaintainRoute = "/v1/search/ann/maintain";
    internal const int Count = 72;
    internal static ImmutableArray<(string Name, string Route, GrainReadKind? ReadKind, OperationKind? CommandKind)> Entries { get; } =
    [
        (AnnMaintain, AnnMaintainRoute, null, OperationKind.MaintainAnnIndex),
        (DocumentsGet, DocumentsGetRoute, GrainReadKind.Document, null),
        (StreamsRead, StreamsReadRoute, GrainReadKind.Stream, null),
        (StreamsReplay, StreamsReplayRoute, GrainReadKind.AggregateReplay, null),
        (EventsRead, EventsReadRoute, GrainReadKind.EventSource, null),
        (SubscriptionsStatus, SubscriptionsStatusRoute, GrainReadKind.Subscription, null),
        (MessagesInspect, MessagesInspectRoute, GrainReadKind.Message, null),
        (QueueTransferInspect, QueueTransferInspectRoute, GrainReadKind.QueueTransfer, null),
        (QueueTransferReceipt, QueueTransferReceiptRoute, GrainReadKind.QueueTransferReceipt, null),
        (ScheduleInspect, ScheduleInspectRoute, GrainReadKind.RecurringSchedule, null),
        (SagaInspect, SagaInspectRoute, GrainReadKind.Saga, null),
        (GraphTraverse, GraphTraverseRoute, GrainReadKind.Traverse, null),
        (GraphShortestPath, GraphShortestPathRoute, GrainReadKind.GraphShortestPath, null),
        (GraphIncomingEdges, GraphIncomingEdgesRoute, GrainReadKind.GraphIncomingEdges, null),
        (QueryGraphPath, QueryGraphPathRoute, GrainReadKind.SqlGraphPath, null),
        (AdminPartitionPlacementBind, AdminPartitionPlacementBindRoute, null, OperationKind.BindAtomicPartitionPlacement),
        (AdminPartitionPlacementRead, AdminPartitionPlacementReadRoute, GrainReadKind.AtomicPartitionPlacement, null),
        (SeriesRead, SeriesReadRoute, GrainReadKind.Samples, null),
        (SeriesLatest, SeriesLatestRoute, GrainReadKind.LatestSample, null),
        (SeriesAggregate, SeriesAggregateRoute, GrainReadKind.AggregateSamples, null),
        (SeriesWindows, SeriesWindowsRoute, GrainReadKind.AggregateSampleWindows, null),
        (SeriesRollup, SeriesRollupRoute, GrainReadKind.SampleRollup, null),
        (SeriesRetention, SeriesRetentionRoute, GrainReadKind.SampleRetention, null),
        (QueryExecute, QueryExecuteRoute, GrainReadKind.Query, null),
        (QuerySearch, QuerySearchRoute, GrainReadKind.SqlGraphSearch, null),
        (QueryAst, QueryAstRoute, GrainReadKind.AstQuery, null),
        (QueryPartitions, QueryPartitionsRoute, GrainReadKind.PartitionQuery, null),
        (QueryCapabilities, QueryCapabilitiesRoute, GrainReadKind.QueryCapabilities, null),
        (ChangesRead, ChangesReadRoute, GrainReadKind.ChangeFeed, null),
        (QueryLiveStart, QueryLiveStartRoute, GrainReadKind.LiveQueryStart, null),
        (QueryLiveRead, QueryLiveReadRoute, GrainReadKind.LiveQueryRead, null),
        (OutboxStatus, OutboxStatusRoute, GrainReadKind.OutboxStatus, null),
        (ProjectionsRead, ProjectionsReadRoute, GrainReadKind.ProjectionBatch, null),
        (SearchExecute, SearchExecuteRoute, GrainReadKind.Search, null),
        (SearchGraph, SearchGraphRoute, GrainReadKind.GraphSearch, null),
        (WaitForIndexProtocol.Tool, WaitForIndexProtocol.Route, GrainReadKind.WaitForIndex, null),
        (AdminBackup, AdminBackupRoute, GrainReadKind.Backup, null),
        (AdminAdmission, AdminAdmissionRoute, GrainReadKind.Admission, null),
        (AdminStatus, AdminStatusRoute, GrainReadKind.NodeStatus, null),
        (DocumentsCommit, DocumentsCommitRoute, null, OperationKind.Batch),
        ("keyload_messages_receive_across_lanes", "/v1/queues/receive-across-lanes", null, OperationKind.ReceiveAcrossLanes),
        (MessagesReceive, MessagesReceiveRoute, null, OperationKind.Receive),
        (MessagesComplete, MessagesCompleteRoute, null, OperationKind.Delivery),
        (MessagesProcess, MessagesProcessRoute, null, OperationKind.Processing),
        (ResourcesConfigure, ResourcesConfigureRoute, null, OperationKind.ConfigureResource),
        (PrincipalsConfigure, PrincipalsConfigureRoute, null, OperationKind.ConfigurePrincipal),
        (CredentialsConfigure, CredentialsConfigureRoute, null, OperationKind.ConfigureApiKey),
        (AdminDispatch, AdminDispatchRoute, null, OperationKind.SetDispatch),
        (SubscriptionsConfigure, SubscriptionsConfigureRoute, null, OperationKind.ConfigureSubscription),
        (SubscriptionsSeek, SubscriptionsSeekRoute, null, OperationKind.SeekSubscription),
        (SubscriptionsReceive, SubscriptionsReceiveRoute, null, OperationKind.ReceiveSubscription),
        (SubscriptionsComplete, SubscriptionsCompleteRoute, null, OperationKind.SubscriptionDelivery),
        (SubscriptionsProcess, SubscriptionsProcessRoute, null, OperationKind.SubscriptionProcessing),
        (SubscriptionsPause, SubscriptionsPauseRoute, null, OperationKind.SetSubscriptionPaused),
        (ProjectionsConfigure, ProjectionsConfigureRoute, null, OperationKind.ConfigureProjectionConsumer),
        (ProjectionsCommit, ProjectionsCommitRoute, null, OperationKind.CommitProjectionBatch),
        (ProjectionsRelease, ProjectionsReleaseRoute, null, OperationKind.ReleaseProjectionConsumer),
        (OutboxPurge, OutboxPurgeRoute, null, OperationKind.PurgeOutbox),
        .. BlobAgentCases.All().Select(item => (item.Name, item.Route, item.ReadKind, item.CommandKind)),
        (DashboardName, DashboardRoute, GrainReadKind.AdminDashboard, null),
        (ResourcesListName, ResourcesListRoute, GrainReadKind.AdminResources, null),
        (QueueBrowseName, QueueBrowseRoute, GrainReadKind.AdminQueue, null),
        (SqlOperationProtocol.ToolName, SqlOperationProtocol.Route, null, null)
    ];
}
