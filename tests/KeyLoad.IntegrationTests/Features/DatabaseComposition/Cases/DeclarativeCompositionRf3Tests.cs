using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class DeclarativeCompositionRf3Tests(ClusterFixture fixture)
{
    private const string Message = "queued-link";
    private const string EdgePrefix = "knowledge-";
    private const string MessagePrefix = "next-";
    private const string ConflictPrefix = "conflict-";
    private const string CancelledWriteDetail = "The write response is unavailable. Retry the same command ID.";

    [Test]
    public async Task AcKl095DeclarativeInsertRefusesMalformedInputThenReplaysBothModelsAcrossColdAuthorityRepair()
    {
        using var deadline = McpCallerDeadline.Create();
        CompositionSqlColdState state;
        DeclarativeCompositionBudgetState budgetState;
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
            var link = new QueueGraphLink(scenario.First, scenario.Second, RelationalSqlRf3Tokens.EdgeLabel);
            var seed = scenario.Command(
                new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId, RelationalSqlRf3Tokens.FirstRow),
                new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.SecondId, RelationalSqlRf3Tokens.SecondRow),
                new EnqueueMessage(RelationalSqlRf3Tokens.Queue, Message, JsonSerializer.Serialize(link, JsonDefaults.Options)));
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed, deadline.Token));
            var source = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(scenario.Inspect(Message), deadline.Token));
            var forwardMutation = new QueueToGraph(RelationalSqlRf3Tokens.Graph, RelationalSqlRf3Tokens.Queue, EdgePrefix);
            var forward = scenario.Command(forwardMutation);
            var forwardSql = DeclarativeCompositionSql.Forward(forward, forwardMutation);
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                fixture.AdminKey, deadline.Token);

            budgetState = await VerifyRefusalsAsync(sdk, mcp, scenario, link, forwardSql, source, deadline.Token);

            var projected = await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, forwardSql, deadline.Token);
            await Assert.That(projected.Mutations).HasSingleItem();
            await Assert.That(projected.CommandId).IsEqualTo(forward.CommandId);
            await SqlRf3Protocol.EqualAsync(projected, await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, forwardSql, deadline.Token));
            await SqlRf3Protocol.EqualAsync(projected, await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CommitAsync(forward, deadline.Token)));
            await VerifyForwardAsync(sdk, mcp, scenario, link, deadline.Token);

            var reverseMutation = new GraphToQueueMutation(RelationalSqlRf3Tokens.Queue,
                RelationalSqlRf3Tokens.Graph, scenario.First, MessagePrefix);
            var reverse = scenario.Command(reverseMutation);
            var reverseSql = DeclarativeCompositionSql.Reverse(reverse, reverseMutation);
            var derivedReceipt = await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, reverseSql, deadline.Token);
            await Assert.That(derivedReceipt.Mutations).HasSingleItem();
            await Assert.That(derivedReceipt.CommandId).IsEqualTo(reverse.CommandId);
            await SqlRf3Protocol.EqualAsync(derivedReceipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, reverseSql, deadline.Token));
            await SqlRf3Protocol.EqualAsync(derivedReceipt, await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CommitAsync(reverse, deadline.Token)));
            state = await CompositionSqlColdCapture.CaptureAsync(sdk, scenario, link,
                forward, projected, reverse, derivedReceipt, deadline.Token);
            await RequireDerivedStateAsync(state, link);

            var changed = DeclarativeCompositionSql.Forward(forward, forwardMutation with
            {
                EdgeIdPrefix = ConflictPrefix
            });
            var conflict = await sdk.ExecuteSqlAsync(changed, deadline.Token);
            await Assert.That(conflict.IsFailed).IsTrue();
            await Assert.That(conflict.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, changed, deadline.Token),
                ErrorCode.Conflict, dispatched: true);
            var unchanged = await CompositionSqlColdCapture.CaptureAsync(sdk, scenario, link,
                forward, projected, reverse, derivedReceipt, deadline.Token);
            await SqlRf3Protocol.EqualAsync(state, unchanged);
            await SqlRf3Protocol.EqualAsync(projected, await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, forwardSql, deadline.Token));
        }
        await DeclarativeCompositionColdTrial.RunAsync(fixture, state, budgetState, deadline.Token);
    }

    private static async Task<DeclarativeCompositionBudgetState> VerifyRefusalsAsync(KeyLoadClient sdk,
        McpOfficialClient mcp, RelationalSqlRf3Scenario scenario, QueueGraphLink link,
        SqlOperationRequest forwardSql, MessageInspection? source, CancellationToken cancellationToken)
    {
        using var cancelledToken = new CancellationTokenSource();
        await cancelledToken.CancelAsync();
        var cancelled = await sdk.ExecuteSqlAsync(forwardSql, cancelledToken.Token);
        await Assert.That(cancelled.IsFailed).IsTrue();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.UnknownWriteOutcome, CancelledWriteDetail), cancelled.Problem);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(
            new(scenario.Partition, RelationalSqlRf3Tokens.Graph, scenario.First), cancellationToken))).Edges).IsEmpty();
        await SqlRf3Protocol.EqualAsync(source, await PublicReadAssertions.MessageAsync(
            await sdk.InspectAsync(scenario.Inspect(Message), cancellationToken)));
        var budgetState = await DeclarativeCompositionBudgetFlow.RunAsync(sdk, mcp, scenario, link, cancellationToken);

        var invalid = forwardSql with
        {
            Sql = forwardSql.Sql + "; SELECT"
        };
        var rejected = await sdk.ExecuteSqlAsync(invalid, cancellationToken);
        await Assert.That(rejected.IsFailed).IsTrue();
        await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, invalid, cancellationToken),
            ErrorCode.Validation, dispatched: false);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(
            new(scenario.Partition, RelationalSqlRf3Tokens.Graph, scenario.First), cancellationToken))).Edges).IsEmpty();
        await SqlRf3Protocol.EqualAsync(source, await PublicReadAssertions.MessageAsync(
            await sdk.InspectAsync(scenario.Inspect(Message), cancellationToken)));

        return budgetState;
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
        var source = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(scenario.Inspect(Message), cancellationToken));
        await Assert.That(source!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(source.Metadata.Attempts).IsEqualTo(RelationalSqlRf3Tokens.NoResults);
        await Assert.That(source.PayloadJson).IsEqualTo(DatabaseCompositionRf3Payload.OrdinalGolden(link));
        await Assert.That(JsonSerializer.Deserialize<QueueGraphLink>(source.PayloadJson!, JsonDefaults.Options)).IsEqualTo(link);
    }

    private static async Task RequireDerivedStateAsync(CompositionSqlColdState state, QueueGraphLink link)
    {
        await Assert.That(state.First.Json).IsEqualTo(RelationalSqlRf3Tokens.FirstRow);
        await Assert.That(state.Second.Json).IsEqualTo(RelationalSqlRf3Tokens.SecondRow);
        await Assert.That(state.Graph.Edges).HasSingleItem();
        await Assert.That(state.Graph.Edges[0].Label).IsEqualTo(RelationalSqlRf3Tokens.EdgeLabel);
        await Assert.That(state.Derived.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(state.Derived.Metadata.Attempts).IsEqualTo(RelationalSqlRf3Tokens.NoResults);
        await Assert.That(state.Derived.Metadata.LeaseOwner).IsNull();
        await Assert.That(JsonSerializer.Deserialize<QueueGraphLink>(state.Derived.PayloadJson!, JsonDefaults.Options)).IsEqualTo(link);
    }
}
