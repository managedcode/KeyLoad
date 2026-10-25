using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.RelationalStorage;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

/// <summary>AC-COMP-004/007: actual SQL .NET and official MCP compose models on Orleans.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class DatabaseCompositionClusterTests(ClusterFixture fixture)
{
    private const string Message = "queued-link";
    private const string EdgePrefix = "knowledge-";
    private const string MessagePrefix = "next-";
    private const string Missing = "missing";
    private const string Marker = "atomic-marker";
    private const int ExpectedEffects = 4;

    [Test]
    public async Task AC_COMP_007_SqlReadsQueuedLinksAndWritesGraphThenOfficialMcpEnqueuesGraphActions()
    {
        using var deadline = McpCallerDeadline.Create();
        CompositionSqlColdState state;
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
            var link = new QueueGraphLink(scenario.First, scenario.Second, RelationalSqlClusterTokens.EdgeLabel);
            var payload = JsonSerializer.Serialize(link, JsonDefaults.Options);
            var command = scenario.Command(
                new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId, RelationalSqlClusterTokens.FirstRow),
                new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.SecondId, RelationalSqlClusterTokens.SecondRow),
                new EnqueueMessage(RelationalSqlClusterTokens.Queue, Message, payload),
                new QueueToGraph(RelationalSqlClusterTokens.Graph, RelationalSqlClusterTokens.Queue, EdgePrefix));
            var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
            var receipt = await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, sql, deadline.Token);
            await Assert.That(receipt.Mutations.Length).IsEqualTo(ExpectedEffects);
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                fixture.AdminKey, deadline.Token);
            await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, sql, deadline.Token));
            await SqlClusterProtocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CommitAsync(command, deadline.Token)));
            await VerifyForwardAsync(sdk, mcp, scenario, link, deadline.Token);
            var reverse = await VerifyReverseAsync(sdk, mcp, scenario, link, deadline.Token);
            state = await CompositionSqlColdCapture.CaptureAsync(sdk, scenario, link, command, receipt,
                reverse.Command, reverse.Receipt, deadline.Token);
        }
        await CompositionSqlColdTrial.RunAsync(fixture, state, deadline.Token);
    }

    [Test]
    public async Task AC_COMP_004_InvalidQueuedReferenceRollsBackEarlierEffectsThroughSqlAndMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        var link = new QueueGraphLink(scenario.First, scenario.Second with
        {
            Id = Missing
        }, RelationalSqlClusterTokens.EdgeLabel);
        var command = scenario.Command(
            new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId, RelationalSqlClusterTokens.FirstRow),
            new PutDocument(RelationalSqlClusterTokens.Documents, Marker, RelationalSqlClusterTokens.EmptyJson),
            new EnqueueMessage(RelationalSqlClusterTokens.Queue, Message, JsonSerializer.Serialize(link, JsonDefaults.Options)),
            new QueueToGraph(RelationalSqlClusterTokens.Graph, RelationalSqlClusterTokens.Queue, EdgePrefix));
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var rejected = await sdk.ExecuteSqlAsync(sql, deadline.Token);
        await Assert.That(rejected.IsFailed).IsTrue();
        await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.NotFound));
        await Assert.That(await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(scenario.First, deadline.Token))).IsNull();
        await Assert.That(await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(
            new(scenario.Partition, RelationalSqlClusterTokens.Documents, Marker), deadline.Token))).IsNull();
        await Assert.That(await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
            scenario.Inspect(Message), deadline.Token))).IsNull();
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var replay = await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, deadline.Token);
        _ = (await McpCallerAssertions.ErrorAsync(replay, ErrorCode.NotFound, dispatched: true)).Value;
    }

    private static async Task VerifyForwardAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlClusterScenario scenario, QueueGraphLink link, CancellationToken cancellationToken)
    {
        var traversal = new TraverseRequest(scenario.Partition, RelationalSqlClusterTokens.Graph, scenario.First);
        var graph = await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(traversal, cancellationToken));
        await Assert.That(graph.Edges).HasSingleItem();
        await Assert.That(graph.Edges[0].Id).IsEqualTo(EdgePrefix + Message);
        await Assert.That(graph.Edges[0].From).IsEqualTo(scenario.StoredFirst);
        await Assert.That(graph.Edges[0].To).IsEqualTo(scenario.StoredSecond);
        await SqlClusterProtocol.EqualAsync(graph, await SqlClusterProtocol.McpAsync<global::KeyLoad.GraphTraversal>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.GraphTraverse, traversal), cancellationToken));
        var source = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(scenario.Inspect(Message), cancellationToken));
        await Assert.That(source!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(source.Metadata.Attempts).IsEqualTo(RelationalSqlClusterTokens.NoResults);
        await Assert.That(source.PayloadJson).IsEqualTo(DatabaseCompositionClusterPayload.Golden(link));
        await Assert.That(JsonSerializer.Deserialize<QueueGraphLink>(source.PayloadJson!, JsonDefaults.Options)).IsEqualTo(link);
    }

    private static async Task<(CommandRequest Command, CommitReceipt Receipt)> VerifyReverseAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlClusterScenario scenario, QueueGraphLink link, CancellationToken cancellationToken)
    {
        var command = scenario.Command(new GraphToQueueMutation(RelationalSqlClusterTokens.Queue,
            RelationalSqlClusterTokens.Graph, scenario.First, MessagePrefix));
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var receipt = await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, sql, cancellationToken);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, sql, cancellationToken));
        var request = scenario.Inspect(MessagePrefix + EdgePrefix + Message);
        var derived = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(request, cancellationToken));
        await Assert.That(derived!.Metadata.State).IsEqualTo(MessageState.Ready);
        var storedLink = link with
        {
            From = scenario.StoredFirst,
            To = scenario.StoredSecond
        };
        await Assert.That(JsonSerializer.Deserialize<QueueGraphLink>(derived.PayloadJson!, JsonDefaults.Options)).IsEqualTo(storedLink);
        await SqlClusterProtocol.EqualAsync(derived, (await SqlClusterProtocol.McpAsync<MessageInspectionResult>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, request), cancellationToken)).Message);
        return (command, receipt);
    }
}
