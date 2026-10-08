using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.GraphTraversal;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>The accepted ADR-039 public schema and effect oracle, independent of server implementation objects.</summary>
internal static class McpCatalogExpectations
{
    private const string TextMaintainName = "keyload_search_text_maintain";
    private static readonly string[] TextRequiredFields = ["commandId", "consumer", "collection", "field", "indexGeneration", "nodeId", "placement", "mode"];
    private const string AnnReadName = "keyload_search_ann_read";
    private static readonly string[] AnnReadFields = ["version", "search", "consumer", "indexGeneration"];
    private const string AnnMaintainName = "keyload_search_ann_maintain";
    private static readonly string[] AnnRequiredFields = ["commandId", "consumer", "collection", "field", "space", "indexGeneration", "nodeId", "placement", "mode"];
    private const string WaitForIndexName = "keyload_search_wait_for_index";
    private const string TextFieldProperty = "textField";
    private const string MinimumTokenProperty = "minimumToken";
    private const string AppliedTokenProperty = "appliedToken";
    private const string SchemaVersionProperty = "schemaVersion";
    private const string PolicyEpochProperty = "policyEpoch";
    private const string MultiLaneReceiveName = "keyload_messages_receive_across_lanes";
    private const string RequestsProperty = "requests";
    private const string DashboardName = "keyload_admin_dashboard";
    private const string ResourcesListName = "keyload_admin_resources_list";
    private const string QueueBrowseName = "keyload_admin_queue_browse";
    internal static ImmutableArray<McpToolExpectation> Entries { get; } =
    [
        .. McpBlobCatalogExpectations.Entries,
        Read(AnnReadName, [.. AnnReadFields]),
        new(AnnMaintainName, false, false, true, McpExpectedBody.Object, false, [.. AnnRequiredFields]),
        new(TextMaintainName, false, false, true, McpExpectedBody.Object, false, [.. TextRequiredFields]),
        Read(McpCallerTools.DocumentsGet, [McpDiscoveryProtocol.Reference]),
        Read(McpCallerTools.StreamsRead, [McpDiscoveryProtocol.Stream]),
        Read(McpCallerTools.StreamsReplay, [McpDiscoveryProtocol.Stream, McpDiscoveryProtocol.ReducerVersion]),
        Read(McpCallerTools.EventsRead, [McpDiscoveryProtocol.Source]),
        Read(McpCallerTools.SubscriptionsStatus, [McpDiscoveryProtocol.Subscription]),
        Read(McpCallerTools.MessagesInspect, [McpDiscoveryProtocol.Lane, McpDiscoveryProtocol.Id]),
        Read(McpCallerTools.QueueTransferInspect, [McpDiscoveryProtocol.SourceQueue, McpDiscoveryProtocol.TransferId]),
        Read(McpCallerTools.QueueTransferReceipt,
            [McpDiscoveryProtocol.DestinationQueue, McpDiscoveryProtocol.SourceQueue, McpDiscoveryProtocol.TransferId]),
        Read(McpCallerTools.ScheduleInspect, [McpDiscoveryProtocol.Lane, McpDiscoveryProtocol.ScheduleId]),
        Read(McpCallerTools.SagaInspect, [McpDiscoveryProtocol.Lane, McpDiscoveryProtocol.SagaId]),
        Read(McpCallerTools.GraphTraverse, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Graph, McpDiscoveryProtocol.Start]),
        Read(McpCallerTools.GraphShortestPath, [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Partition,
            McpDiscoveryProtocol.Graph, McpDiscoveryProtocol.FromEntity, McpDiscoveryProtocol.To],
            [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Found, McpDiscoveryProtocol.Hops,
                McpDiscoveryProtocol.Vertices, McpDiscoveryProtocol.Edges, McpDiscoveryProtocol.CutPosition]),
        Read(McpCallerTools.GraphIncomingEdges, [GraphIncomingMcpProtocol.Version, GraphIncomingMcpProtocol.Target,
            GraphIncomingMcpProtocol.Graph, GraphIncomingMcpProtocol.Limit],
            [GraphIncomingMcpProtocol.Version, GraphIncomingMcpProtocol.Rows, GraphIncomingMcpProtocol.CutPosition,
                GraphIncomingMcpProtocol.Projection]),
        Read(McpCallerTools.SeriesRead, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set, McpDiscoveryProtocol.SeriesId, McpDiscoveryProtocol.From, McpDiscoveryProtocol.Until]),
        Read(McpCallerTools.SeriesLatest, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set, McpDiscoveryProtocol.SeriesId]),
        Read(McpCallerTools.SeriesAggregate, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set, McpDiscoveryProtocol.SeriesId, McpDiscoveryProtocol.From]),
        Read(McpCallerTools.SeriesWindows, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set,
            McpDiscoveryProtocol.SeriesId, McpDiscoveryProtocol.From, McpDiscoveryProtocol.UntilExclusive, McpDiscoveryProtocol.Width]),
        Read(SampleRollupProtocol.ReadTool, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set,
            McpDiscoveryProtocol.SeriesId, McpDiscoveryProtocol.From, McpDiscoveryProtocol.UntilExclusive]),
        Read(McpCallerTools.SeriesRetention, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set,
            McpDiscoveryProtocol.SeriesId]),
        Read(McpCallerTools.QueryExecute, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Sql]),
        Read(McpCallerTools.QuerySearch, [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Query]),
        Read(McpCallerTools.QueryGraphPath, [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Query],
            [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Found, McpDiscoveryProtocol.Hops,
                McpDiscoveryProtocol.Vertices, McpDiscoveryProtocol.Edges, McpDiscoveryProtocol.CutPosition]),
        Read(McpCallerTools.QueryPartitions, [PartitionQueryMcpProtocol.Version, PartitionQueryMcpProtocol.Partitions, PartitionQueryMcpProtocol.Query,
                PartitionQueryMcpProtocol.Parameters, PartitionQueryMcpProtocol.AllowFullScan, PartitionQueryMcpProtocol.AstVersion],
            [PartitionQueryMcpProtocol.Version, PartitionQueryMcpProtocol.Rows, PartitionQueryMcpProtocol.Leaves,
                PartitionQueryMcpProtocol.Complete]),
        new(McpCallerTools.AdminPartitionPlacementBind, false, true, false, McpExpectedBody.Object, true,
            [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.ExpectedRevision, McpDiscoveryProtocol.Partition,
                McpDiscoveryProtocol.PhysicalShardId]),
        Read(McpCallerTools.AdminPartitionPlacementRead,
            [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Partition],
            [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.PhysicalShardId,
                McpDiscoveryProtocol.Incarnation, McpDiscoveryProtocol.VoterIds, McpDiscoveryProtocol.PlacementEpoch,
                McpDiscoveryProtocol.DirectoryRevision, McpDiscoveryProtocol.Revision, McpDiscoveryProtocol.IsFallback]),
        Read(McpCallerTools.QueryAst, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Query]),
        Empty(McpCallerTools.QueryCapabilities),
        Read(McpCallerTools.ChangesRead, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Collection]),
        Read(McpCallerTools.QueryLiveStart, [McpDiscoveryProtocol.Query]),
        Read(McpCallerTools.QueryLiveRead, [McpDiscoveryProtocol.Query, McpDiscoveryProtocol.Cursor]),
        Read(McpCallerTools.OutboxStatus, [McpDiscoveryProtocol.Partition]),
        Read(McpCallerTools.ProjectionsRead, [McpDiscoveryProtocol.Consumer]),
        Read(WaitForIndexName, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Collection,
            TextFieldProperty, MinimumTokenProperty],
            [AppliedTokenProperty, SchemaVersionProperty, PolicyEpochProperty]),
        Read(McpCallerTools.SearchExecute, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Collection]),
        Read(McpCallerTools.SearchGraph, [McpDiscoveryProtocol.Version, McpDiscoveryProtocol.Search]),
        new(McpCallerTools.AdminBackup, false, false, false, McpExpectedBody.None, false, []),
        Empty(McpCallerTools.AdminAdmission),
        Empty(McpCallerTools.AdminStatus),
        Empty(DashboardName),
        Read(ResourcesListName, [McpDiscoveryProtocol.TenantId, McpDiscoveryProtocol.DatabaseId]),
        Read(QueueBrowseName, [McpDiscoveryProtocol.Lane]),
        Write(McpCallerTools.DocumentsCommit, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Mutations]),
        new(MultiLaneReceiveName, false, false, true, McpExpectedBody.Object, false,
            [McpCallerProtocol.RequestId, RequestsProperty]),
        Write(McpCallerTools.MessagesReceive, [McpCallerProtocol.RequestId, McpDiscoveryProtocol.Lane]),
        Write(McpCallerTools.MessagesComplete, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Lane, McpDiscoveryProtocol.Token, McpDiscoveryProtocol.Action]),
        Write(McpCallerTools.MessagesProcess, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Lane, McpDiscoveryProtocol.Token, McpDiscoveryProtocol.HandlerScope, McpDiscoveryProtocol.ExecutionGeneration, McpDiscoveryProtocol.Effects]),
        new(McpCallerTools.ResourcesConfigure, false, true, false, McpExpectedBody.Object, true,
            [McpDiscoveryProtocol.TenantId, McpDiscoveryProtocol.DatabaseId, McpDiscoveryProtocol.Definition]),
        Header(McpCallerTools.PrincipalsConfigure, [McpDiscoveryProtocol.Principal]),
        Header(McpCallerTools.CredentialsConfigure, [McpDiscoveryProtocol.ApiKey]),
        new(McpCallerTools.AdminDispatch, false, true, true, McpExpectedBody.Boolean, true, []),
        Write(McpCallerTools.SubscriptionsConfigure, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Subscription, McpDiscoveryProtocol.Definition]),
        Write(McpCallerTools.SubscriptionsSeek, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Subscription, McpDiscoveryProtocol.ExpectedGeneration, McpDiscoveryProtocol.Start]),
        Write(McpCallerTools.SubscriptionsReceive, [McpCallerProtocol.RequestId, McpDiscoveryProtocol.Subscription]),
        Write(McpCallerTools.SubscriptionsComplete, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Subscription, McpDiscoveryProtocol.Token, McpDiscoveryProtocol.Action]),
        Write(McpCallerTools.SubscriptionsProcess, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Subscription, McpDiscoveryProtocol.Token, McpDiscoveryProtocol.HandlerScope, McpDiscoveryProtocol.ExecutionGeneration, McpDiscoveryProtocol.Effects]),
        Write(McpCallerTools.SubscriptionsPause, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Subscription, McpDiscoveryProtocol.ExpectedGeneration, McpDiscoveryProtocol.Paused]),
        Write(McpCallerTools.ProjectionsConfigure, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Consumer, McpDiscoveryProtocol.Definition]),
        Write(McpCallerTools.ProjectionsCommit, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Consumer, McpDiscoveryProtocol.Token, McpDiscoveryProtocol.Effects]),
        Write(McpCallerTools.ProjectionsRelease, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Consumer, McpDiscoveryProtocol.IndexGeneration]),
        Write(McpCallerTools.OutboxPurge, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.ThroughSequence]),
        new(SqlOperationProtocol.ToolName, false, false, true, McpExpectedBody.Object, false,
            [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Sql])
    ];

    private static McpToolExpectation Read(string name, ImmutableArray<string> fields,
        ImmutableArray<string> resultFields = default)
        => new(name, true, true, false, McpExpectedBody.Object, false, fields, resultFields);
    private static McpToolExpectation Empty(string name) => new(name, true, true, false, McpExpectedBody.None, false, []);
    private static McpToolExpectation Write(string name, ImmutableArray<string> fields) => new(name, false, true, true, McpExpectedBody.Object, false, fields);
    private static McpToolExpectation Header(string name, ImmutableArray<string> fields) => new(name, false, true, true, McpExpectedBody.Object, true, fields);
}
