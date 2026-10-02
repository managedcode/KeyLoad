using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>The accepted ADR-039 public schema and effect oracle, independent of server implementation objects.</summary>
internal static class McpCatalogExpectations
{
    private const string DashboardName = "keyload_admin_dashboard";
    private const string ResourcesListName = "keyload_admin_resources_list";
    private const string QueueBrowseName = "keyload_admin_queue_browse";
    internal static ImmutableArray<McpToolExpectation> Entries { get; } =
    [
        .. McpBlobCatalogExpectations.Entries,
        Read(McpCallerTools.DocumentsGet, [McpDiscoveryProtocol.Reference]),
        Read(McpCallerTools.StreamsRead, [McpDiscoveryProtocol.Stream]),
        Read(McpCallerTools.EventsRead, [McpDiscoveryProtocol.Source]),
        Read(McpCallerTools.SubscriptionsStatus, [McpDiscoveryProtocol.Subscription]),
        Read(McpCallerTools.MessagesInspect, [McpDiscoveryProtocol.Lane, McpDiscoveryProtocol.Id]),
        Read(McpCallerTools.GraphTraverse, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Graph, McpDiscoveryProtocol.Start]),
        Read(McpCallerTools.SeriesRead, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set, McpDiscoveryProtocol.SeriesId, McpDiscoveryProtocol.From, McpDiscoveryProtocol.Until]),
        Read(McpCallerTools.SeriesLatest, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set, McpDiscoveryProtocol.SeriesId]),
        Read(McpCallerTools.SeriesAggregate, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set, McpDiscoveryProtocol.SeriesId, McpDiscoveryProtocol.From]),
        Read(McpCallerTools.SeriesWindows, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Set,
            McpDiscoveryProtocol.SeriesId, McpDiscoveryProtocol.From, McpDiscoveryProtocol.UntilExclusive, McpDiscoveryProtocol.Width]),
        Read(McpCallerTools.QueryExecute, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Sql]),
        Read(McpCallerTools.QueryAst, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Query]),
        Empty(McpCallerTools.QueryCapabilities),
        Read(McpCallerTools.ChangesRead, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Collection]),
        Read(McpCallerTools.QueryLiveStart, [McpDiscoveryProtocol.Query]),
        Read(McpCallerTools.QueryLiveRead, [McpDiscoveryProtocol.Query, McpDiscoveryProtocol.Cursor]),
        Read(McpCallerTools.OutboxStatus, [McpDiscoveryProtocol.Partition]),
        Read(McpCallerTools.ProjectionsRead, [McpDiscoveryProtocol.Consumer]),
        Read(McpCallerTools.SearchExecute, [McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Collection]),
        new(McpCallerTools.AdminBackup, false, false, false, McpExpectedBody.None, false, []),
        Empty(McpCallerTools.AdminAdmission),
        Empty(McpCallerTools.AdminStatus),
        Empty(DashboardName),
        Read(ResourcesListName, [McpDiscoveryProtocol.TenantId, McpDiscoveryProtocol.DatabaseId]),
        Read(QueueBrowseName, [McpDiscoveryProtocol.Lane]),
        Write(McpCallerTools.DocumentsCommit, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.Mutations]),
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
        Write(McpCallerTools.OutboxPurge, [McpCallerProtocol.CommandId, McpDiscoveryProtocol.Partition, McpDiscoveryProtocol.ThroughSequence])
    ];

    private static McpToolExpectation Read(string name, ImmutableArray<string> fields) => new(name, true, true, false, McpExpectedBody.Object, false, fields);
    private static McpToolExpectation Empty(string name) => new(name, true, true, false, McpExpectedBody.None, false, []);
    private static McpToolExpectation Write(string name, ImmutableArray<string> fields) => new(name, false, true, true, McpExpectedBody.Object, false, fields);
    private static McpToolExpectation Header(string name, ImmutableArray<string> fields) => new(name, false, true, true, McpExpectedBody.Object, true, fields);
}
