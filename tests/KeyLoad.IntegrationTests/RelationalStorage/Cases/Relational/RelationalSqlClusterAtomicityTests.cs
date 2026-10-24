using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.RelationalStorage;

/// <summary>AC-AISQL-003/004: execution typed-row failures roll back every earlier mixed-model effect.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlClusterAtomicityTests(ClusterFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql004FailedSqlTableDocumentEventQueueBatchHasNoPartialEffects(bool uniqueConflict)
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.Command(new PutDocument(
            RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId, RelationalSqlClusterTokens.FirstRow)), deadline.Token));
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, deadline.Token));
        var invalidId = uniqueConflict ? RelationalSqlClusterTokens.SecondId : RelationalSqlClusterTokens.InvalidId;
        var invalidRow = uniqueConflict ? RelationalSqlClusterTokens.DuplicateTitleRow : RelationalSqlClusterTokens.InvalidTypeRow;
        var command = scenario.Command(
            new PutDocument(RelationalSqlClusterTokens.Documents, RelationalSqlClusterTokens.RolledBackId, RelationalSqlClusterTokens.EmptyJson),
            new AppendEvents(RelationalSqlClusterTokens.Streams, RelationalSqlClusterTokens.RolledBackId,
                [new(RelationalSqlClusterTokens.EventId, RelationalSqlClusterTokens.EventType, RelationalSqlClusterTokens.EmptyJson)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(RelationalSqlClusterTokens.Queue, RelationalSqlClusterTokens.RolledBackId, RelationalSqlClusterTokens.EmptyJson),
            new PutDocument(RelationalSqlClusterTokens.Table, invalidId, invalidRow));
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var error = uniqueConflict ? ErrorCode.Conflict : ErrorCode.Validation;
        var result = await sdk.ExecuteSqlAsync(sql, deadline.Token);
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(error.ToString());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, deadline.Token), error, dispatched: true);
        await VerifyRollbackAsync(sdk, mcp, scenario, invalidId, deadline.Token);
        await SqlClusterProtocol.EqualAsync(before, await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, deadline.Token)));
    }

    private static async Task VerifyRollbackAsync(KeyLoadClient sdk, McpOfficialClient mcp, RelationalSqlClusterScenario scenario,
        string invalidId, CancellationToken cancellationToken)
    {
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(
            new(scenario.Partition, RelationalSqlClusterTokens.Documents, RelationalSqlClusterTokens.RolledBackId), cancellationToken))).IsNull();
        var invalid = new GetDocumentRequest(new(scenario.Partition, RelationalSqlClusterTokens.Table, invalidId));
        await Assert.That(await SqlClusterProtocol.McpAsync<DocumentResult?>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsGet, invalid), cancellationToken)).IsNull();
        var queue = scenario.Inspect(RelationalSqlClusterTokens.RolledBackId);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(queue, cancellationToken))).IsNull();
        await Assert.That(await SqlClusterProtocol.McpAsync<MessageInspection?>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, queue), cancellationToken)).IsNull();
        var request = scenario.ReadStream(RelationalSqlClusterTokens.RolledBackId);
        var stream = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(request, cancellationToken));
        var official = await SqlClusterProtocol.McpAsync<StreamPage>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request), cancellationToken);
        await Assert.That(stream.Events).IsEmpty();
        await Assert.That(official.Events).IsEmpty();
        await Assert.That(stream.Head.TailRevision).IsEqualTo(RelationalSqlClusterTokens.InitialEventRevision);
        await Assert.That(official.Head.TailRevision).IsEqualTo(RelationalSqlClusterTokens.InitialEventRevision);
    }
}
