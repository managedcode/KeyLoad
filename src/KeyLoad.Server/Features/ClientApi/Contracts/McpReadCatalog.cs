using System.Collections.Immutable;
using KeyLoad.Orleans;
using KeyLoad.Query;

namespace KeyLoad.Server;

/// <summary>Explicit typed bindings for the frozen read capabilities.</summary>
internal static class McpReadCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries { get; } =
    [
        McpOperationFactory.Read<GetDocumentRequest, DocumentResult>(McpToolNames.DocumentsGet, McpToolRoutes.DocumentsGet, GrainReadKind.Document, true),
        McpOperationFactory.Read<ReadStreamRequest, StreamPage>(McpToolNames.StreamsRead, McpToolRoutes.StreamsRead, GrainReadKind.Stream),
        McpOperationFactory.Read<ReadAggregateReplayRequest, AggregateReplayPage>(McpToolNames.StreamsReplay,
            McpToolRoutes.StreamsReplay, GrainReadKind.AggregateReplay),
        McpOperationFactory.Read<ReadEventSourceRequest, EventSourcePage>(McpToolNames.EventsRead, McpToolRoutes.EventsRead, GrainReadKind.EventSource),
        McpOperationFactory.Read<GetSubscriptionRequest, SubscriptionInfo>(McpToolNames.SubscriptionsStatus, McpToolRoutes.SubscriptionsStatus, GrainReadKind.Subscription),
        McpOperationFactory.Read<InspectMessageRequest, MessageInspection>(McpToolNames.MessagesInspect, McpToolRoutes.MessagesInspect, GrainReadKind.Message, true),
        McpOperationFactory.Read<InspectQueueTransferRequest, QueueTransferInspection>(McpToolNames.QueueTransferInspect,
            McpToolRoutes.QueueTransferInspect, GrainReadKind.QueueTransfer, true),
        McpOperationFactory.Read<InspectQueueTransferReceiptRequest, QueueTransferReceiptInspection>(McpToolNames.QueueTransferReceipt,
            McpToolRoutes.QueueTransferReceipt, GrainReadKind.QueueTransferReceipt, true),
        McpOperationFactory.Read<InspectRecurringScheduleRequest, RecurringScheduleInspection>(McpToolNames.ScheduleInspect,
            McpToolRoutes.ScheduleInspect, GrainReadKind.RecurringSchedule, true),
        McpOperationFactory.Read<InspectSagaRequest, SagaInspection>(McpToolNames.SagaInspect, McpToolRoutes.SagaInspect, GrainReadKind.Saga, true),
        McpOperationFactory.Read<TraverseRequest, GraphTraversal>(McpToolNames.GraphTraverse, McpToolRoutes.GraphTraverse, GrainReadKind.Traverse),
        McpOperationFactory.Read<GraphShortestPathRequest, GraphShortestPathResult>(McpToolNames.GraphShortestPath,
            McpToolRoutes.GraphShortestPath, GrainReadKind.GraphShortestPath),
        McpOperationFactory.Read<ReadIncomingGraphEdgesRequestV1, GraphIncomingEdgesPageV1>(McpToolNames.GraphIncomingEdges,
            McpToolRoutes.GraphIncomingEdges, GrainReadKind.GraphIncomingEdges),
        McpOperationFactory.Read<ReadSamplesRequest, SampleRecord[]>(McpToolNames.SeriesRead, McpToolRoutes.SeriesRead, GrainReadKind.Samples),
        McpOperationFactory.Read<ReadLatestSampleRequest, LatestSampleResult>(McpToolNames.SeriesLatest, McpToolRoutes.SeriesLatest, GrainReadKind.LatestSample),
        McpOperationFactory.Read<AggregateSamplesRequest, SampleAggregate>(McpToolNames.SeriesAggregate, McpToolRoutes.SeriesAggregate, GrainReadKind.AggregateSamples),
        McpOperationFactory.Read<AggregateSampleWindowsRequest, SampleAggregateWindowsResult>(McpToolNames.SeriesWindows,
            McpToolRoutes.SeriesWindows, GrainReadKind.AggregateSampleWindows),
        McpOperationFactory.Read<ReadSampleRollupRequest, SampleRollupResult>(McpToolNames.SeriesRollup,
            McpToolRoutes.SeriesRollup, GrainReadKind.SampleRollup),
        McpOperationFactory.Read<ReadSampleRetentionRequest, SampleRetentionStatus>(McpToolNames.SeriesRetention,
            McpToolRoutes.SeriesRetention, GrainReadKind.SampleRetention),
        McpOperationFactory.Read<QueryRequest, QueryPage>(McpToolNames.QueryExecute, McpToolRoutes.QueryExecute, GrainReadKind.Query),
        McpOperationFactory.Read<SqlGraphSearchRequest, GraphSearchResult>(McpToolNames.QuerySearch,
            McpToolRoutes.QuerySearch, GrainReadKind.SqlGraphSearch),
        McpOperationFactory.Read<SqlGraphPathRequest, GraphShortestPathResult>(McpToolNames.QueryGraphPath,
            McpToolRoutes.QueryGraphPath, GrainReadKind.SqlGraphPath),
        McpOperationFactory.Read<AstQueryRequest, QueryPage>(McpToolNames.QueryAst, McpToolRoutes.QueryAst, GrainReadKind.AstQuery),
        McpOperationFactory.Read<PartitionQueryRequestV1, PartitionQueryPageV1>(McpToolNames.QueryPartitions,
            McpToolRoutes.QueryPartitions, GrainReadKind.PartitionQuery),
        McpOperationFactory.Read<QueryCapabilityManifest>(McpToolNames.QueryCapabilities, McpToolRoutes.QueryCapabilities, GrainReadKind.QueryCapabilities),
        McpOperationFactory.Read<ReadChangeFeedRequest, ChangeFeedPage>(McpToolNames.ChangesRead, McpToolRoutes.ChangesRead, GrainReadKind.ChangeFeed),
        McpOperationFactory.Read<StartLiveQueryRequest, LiveQuerySnapshot>(McpToolNames.QueryLiveStart, McpToolRoutes.QueryLiveStart, GrainReadKind.LiveQueryStart),
        McpOperationFactory.Read<ReadLiveQueryRequest, LiveQueryPage>(McpToolNames.QueryLiveRead, McpToolRoutes.QueryLiveRead, GrainReadKind.LiveQueryRead),
        McpOperationFactory.Read<GetOutboxStatusRequest, OutboxStatus>(McpToolNames.OutboxStatus, McpToolRoutes.OutboxStatus, GrainReadKind.OutboxStatus),
        McpOperationFactory.Read<ReadProjectionBatchRequest, ProjectionBatch>(McpToolNames.ProjectionsRead, McpToolRoutes.ProjectionsRead, GrainReadKind.ProjectionBatch),
        McpOperationFactory.Read<WaitForIndexRequest, WaitForIndexResult>(WaitForIndexProtocol.Tool, WaitForIndexProtocol.Route, GrainReadKind.WaitForIndex),
        McpOperationFactory.Read<ApproximateSearchRequest, AnnSearchPage>(AnnSearchProtocol.Tool, AnnSearchProtocol.Route, GrainReadKind.ApproximateSearch),
        McpOperationFactory.Read<SearchRequest, RankedDocument[]>(McpToolNames.SearchExecute, McpToolRoutes.SearchExecute, GrainReadKind.Search),
        McpOperationFactory.Read<GraphSearchRequest, GraphSearchResult>(McpToolNames.SearchGraph, McpToolRoutes.SearchGraph, GrainReadKind.GraphSearch),
        McpOperationFactory.Read<BackupReceipt>(McpToolNames.AdminBackup, McpToolRoutes.AdminBackup, GrainReadKind.Backup),
        McpOperationFactory.Read<NodeAdmissionStatus>(McpToolNames.AdminAdmission, McpToolRoutes.AdminAdmission, GrainReadKind.Admission),
        McpOperationFactory.Read<NodeStatus>(McpToolNames.AdminStatus, McpToolRoutes.AdminStatus, GrainReadKind.NodeStatus),
        McpOperationFactory.Read<AtomicPartitionPlacementReadRequest, AtomicPartitionPlacementResolution>(
            McpToolNames.AdminPartitionPlacementRead, McpToolRoutes.AdminPartitionPlacementRead,
            GrainReadKind.AtomicPartitionPlacement)
    ];
}
