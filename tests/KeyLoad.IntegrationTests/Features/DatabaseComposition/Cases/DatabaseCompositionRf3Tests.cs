using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>AC-COMP-004/007: actual SQL .NET and official MCP compose models on RF3.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class DatabaseCompositionRf3Tests(ClusterFixture fixture)
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
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        var link = new QueueGraphLink(scenario.First, scenario.Second, RelationalSqlRf3Tokens.EdgeLabel);
        var payload = JsonSerializer.Serialize(link, JsonDefaults.Options);
        var command = scenario.Command(
            new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId, RelationalSqlRf3Tokens.FirstRow),
            new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.SecondId, RelationalSqlRf3Tokens.SecondRow),
            new EnqueueMessage(RelationalSqlRf3Tokens.Queue, Message, payload),
            new QueueToGraph(RelationalSqlRf3Tokens.Graph, RelationalSqlRf3Tokens.Queue, EdgePrefix));
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var receipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, deadline.Token);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(ExpectedEffects);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, sql, deadline.Token));
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitAsync(command, deadline.Token)));
        await VerifyForwardAsync(sdk, mcp, scenario, link, deadline.Token);
        await VerifyReverseAsync(sdk, mcp, scenario, link, deadline.Token);
    }

    [Test]
    public async Task AC_COMP_004_InvalidQueuedReferenceRollsBackEarlierEffectsThroughSqlAndMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        var link = new QueueGraphLink(scenario.First, scenario.Second with { Id = Missing }, RelationalSqlRf3Tokens.EdgeLabel);
        var command = scenario.Command(
            new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId, RelationalSqlRf3Tokens.FirstRow),
            new PutDocument(RelationalSqlRf3Tokens.Documents, Marker, RelationalSqlRf3Tokens.EmptyJson),
            new EnqueueMessage(RelationalSqlRf3Tokens.Queue, Message, JsonSerializer.Serialize(link, JsonDefaults.Options)),
            new QueueToGraph(RelationalSqlRf3Tokens.Graph, RelationalSqlRf3Tokens.Queue, EdgePrefix));
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var rejected = await sdk.ExecuteSqlAsync(sql, deadline.Token);
        await Assert.That(rejected.IsFailed).IsTrue();
        await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.NotFound));
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, deadline.Token))).IsNull();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(
            new(scenario.Partition, RelationalSqlRf3Tokens.Documents, Marker), deadline.Token))).IsNull();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            scenario.Inspect(Message), deadline.Token))).IsNull();
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var replay = await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, deadline.Token);
        await McpCallerAssertions.ErrorAsync(replay, ErrorCode.NotFound, dispatched: true);
    }

    private static async Task VerifyForwardAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlRf3Scenario scenario, QueueGraphLink link, CancellationToken cancellationToken)
    {
        var traversal = new TraverseRequest(scenario.Partition, RelationalSqlRf3Tokens.Graph, scenario.First);
        var graph = await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(traversal, cancellationToken));
        await Assert.That(graph.Edges).HasSingleItem();
        await Assert.That(graph.Edges[0].Id).IsEqualTo(EdgePrefix + Message);
        await Assert.That(graph.Edges[0].From).IsEqualTo(scenario.First);
        await Assert.That(graph.Edges[0].To).IsEqualTo(scenario.Second);
        await SqlRf3Protocol.EqualAsync(graph, await SqlRf3Protocol.McpAsync<global::KeyLoad.GraphTraversal>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.GraphTraverse, traversal), cancellationToken));
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(scenario.Inspect(Message), cancellationToken));
        await Assert.That(source!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(source.Metadata.Attempts).IsEqualTo(RelationalSqlRf3Tokens.NoResults);
        await Assert.That(source.PayloadJson).IsEqualTo(DatabaseCompositionRf3Payload.OrdinalGolden(link));
        await Assert.That(JsonSerializer.Deserialize<QueueGraphLink>(source.PayloadJson!, JsonDefaults.Options)).IsEqualTo(link);
    }

    private static async Task VerifyReverseAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlRf3Scenario scenario, QueueGraphLink link, CancellationToken cancellationToken)
    {
        var command = scenario.Command(new GraphToQueueMutation(RelationalSqlRf3Tokens.Queue,
            RelationalSqlRf3Tokens.Graph, scenario.First, MessagePrefix));
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var receipt = await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, sql, cancellationToken);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, cancellationToken));
        var request = scenario.Inspect(MessagePrefix + EdgePrefix + Message);
        var derived = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        await Assert.That(derived!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(JsonSerializer.Deserialize<QueueGraphLink>(derived.PayloadJson!, JsonDefaults.Options)).IsEqualTo(link);
        await SqlRf3Protocol.EqualAsync(derived, await SqlRf3Protocol.McpAsync<MessageInspection>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, request), cancellationToken));
    }
}
