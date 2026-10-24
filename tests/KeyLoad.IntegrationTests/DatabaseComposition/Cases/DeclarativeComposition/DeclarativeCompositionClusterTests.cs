using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.RelationalStorage;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class DeclarativeCompositionClusterTests(ClusterFixture fixture)
{
    private const string Message = "queued-link";
    private const string EdgePrefix = "knowledge-";
    private const string MessagePrefix = "next-";
    private const string ConflictPrefix = "conflict-";
    private const string CancelledWriteDetail = "The write response is unavailable. Retry the same command ID.";

    [Test]
    public async Task DeclarativeInsertRefusesMalformedInputThenReplaysBothModelsAcrossColdAuthorityRepair()
    {
        using var deadline = McpCallerDeadline.Create();
        CompositionSqlColdState state;
        DeclarativeCompositionBudgetState budgetState;
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
            var link = new QueueGraphLink(scenario.First, scenario.Second, RelationalSqlClusterTokens.EdgeLabel);
            var seed = scenario.Command(
                new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId, RelationalSqlClusterTokens.FirstRow),
                new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.SecondId, RelationalSqlClusterTokens.SecondRow),
                new EnqueueMessage(RelationalSqlClusterTokens.Queue, Message, JsonSerializer.Serialize(link, JsonDefaults.Options)));
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed, deadline.Token));
            var source = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(scenario.Inspect(Message), deadline.Token));
            var forwardMutation = new QueueToGraph(RelationalSqlClusterTokens.Graph, RelationalSqlClusterTokens.Queue, EdgePrefix);
            var forward = scenario.Command(forwardMutation);
            var forwardSql = DeclarativeCompositionSql.Forward(forward, forwardMutation);
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                fixture.AdminKey, deadline.Token);

            budgetState = await VerifyRefusalsAsync(sdk, mcp, scenario, link, forwardSql, source, deadline.Token);

            var projected = await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, forwardSql, deadline.Token);
            await Assert.That(projected.Mutations).HasSingleItem();
            await Assert.That(projected.CommandId).IsEqualTo(forward.CommandId);
            await SqlClusterProtocol.EqualAsync(projected, await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, forwardSql, deadline.Token));
            await SqlClusterProtocol.EqualAsync(projected, await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CommitAsync(forward, deadline.Token)));
            await VerifyForwardAsync(sdk, mcp, scenario, link, deadline.Token);

            var reverseMutation = new GraphToQueueMutation(RelationalSqlClusterTokens.Queue,
                RelationalSqlClusterTokens.Graph, scenario.First, MessagePrefix);
            var reverse = scenario.Command(reverseMutation);
            var reverseSql = DeclarativeCompositionSql.Reverse(reverse, reverseMutation);
            var derivedReceipt = await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, reverseSql, deadline.Token);
            await Assert.That(derivedReceipt.Mutations).HasSingleItem();
            await Assert.That(derivedReceipt.CommandId).IsEqualTo(reverse.CommandId);
            await SqlClusterProtocol.EqualAsync(derivedReceipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, reverseSql, deadline.Token));
            await SqlClusterProtocol.EqualAsync(derivedReceipt, await McpCallerAssertions.SdkSuccessAsync(
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
            await SqlClusterProtocol.EqualAsync(state, unchanged);
            await SqlClusterProtocol.EqualAsync(projected, await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, forwardSql, deadline.Token));
        }
        await DeclarativeCompositionColdTrial.RunAsync(fixture, state, budgetState, deadline.Token);
    }

    private static async Task<DeclarativeCompositionBudgetState> VerifyRefusalsAsync(KeyLoadClient sdk,
        McpOfficialClient mcp, RelationalSqlClusterScenario scenario, QueueGraphLink link,
        SqlOperationRequest forwardSql, MessageInspection? source, CancellationToken cancellationToken)
    {
        using var cancelledToken = new CancellationTokenSource();
        await cancelledToken.CancelAsync();
        var cancelled = await sdk.ExecuteSqlAsync(forwardSql, cancelledToken.Token);
        await Assert.That(cancelled.IsFailed).IsTrue();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.UnknownWriteOutcome, CancelledWriteDetail), cancelled.Problem);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(
            new(scenario.Partition, RelationalSqlClusterTokens.Graph, scenario.First), cancellationToken))).Edges).IsEmpty();
        await SqlClusterProtocol.EqualAsync(source, await PublicReadAssertions.MessageAsync(
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
            new(scenario.Partition, RelationalSqlClusterTokens.Graph, scenario.First), cancellationToken))).Edges).IsEmpty();
        await SqlClusterProtocol.EqualAsync(source, await PublicReadAssertions.MessageAsync(
            await sdk.InspectAsync(scenario.Inspect(Message), cancellationToken)));

        return budgetState;
    }

    private static async Task VerifyForwardAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlClusterScenario scenario, QueueGraphLink link, CancellationToken cancellationToken)
    {
        var traversal = new TraverseRequest(scenario.Partition, RelationalSqlClusterTokens.Graph, scenario.First);
        var graph = await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(traversal, cancellationToken));
        await Assert.That(graph.Edges).HasSingleItem();
        await Assert.That(graph.Edges[0].Id).IsEqualTo(EdgePrefix + Message);
        await Assert.That(graph.Edges[0].From).IsEqualTo(scenario.First);
        await Assert.That(graph.Edges[0].To).IsEqualTo(scenario.Second);
        await SqlClusterProtocol.EqualAsync(graph, await SqlClusterProtocol.McpAsync<global::KeyLoad.GraphTraversal>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.GraphTraverse, traversal), cancellationToken));
        var source = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(scenario.Inspect(Message), cancellationToken));
        await Assert.That(source!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(source.Metadata.Attempts).IsEqualTo(RelationalSqlClusterTokens.NoResults);
        await Assert.That(source.PayloadJson).IsEqualTo(DatabaseCompositionClusterPayload.Golden(link));
        await Assert.That(JsonSerializer.Deserialize<QueueGraphLink>(source.PayloadJson!, JsonDefaults.Options)).IsEqualTo(link);
    }

    private static async Task RequireDerivedStateAsync(CompositionSqlColdState state, QueueGraphLink link)
    {
        await Assert.That(state.First.Json).IsEqualTo(RelationalSqlClusterTokens.FirstRow);
        await Assert.That(state.Second.Json).IsEqualTo(RelationalSqlClusterTokens.SecondRow);
        await Assert.That(state.Graph.Edges).HasSingleItem();
        await Assert.That(state.Graph.Edges[0].Label).IsEqualTo(RelationalSqlClusterTokens.EdgeLabel);
        await Assert.That(state.Derived.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(state.Derived.Metadata.Attempts).IsEqualTo(RelationalSqlClusterTokens.NoResults);
        await Assert.That(state.Derived.Metadata.LeaseOwner).IsNull();
        await Assert.That(JsonSerializer.Deserialize<QueueGraphLink>(state.Derived.PayloadJson!, JsonDefaults.Options)).IsEqualTo(link);
    }
}
