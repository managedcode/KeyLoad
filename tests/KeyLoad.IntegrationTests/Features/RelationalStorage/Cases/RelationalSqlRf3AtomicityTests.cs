using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>AC-AISQL-003/004: native typed-row failures roll back every earlier mixed-model effect.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlRf3AtomicityTests(ClusterFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcAisql004FailedSqlTableDocumentEventQueueBatchHasNoPartialEffects(bool uniqueConflict)
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.Command(new PutDocument(
            RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId, RelationalSqlRf3Tokens.FirstRow)), deadline.Token));
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, deadline.Token));
        var invalidId = uniqueConflict ? RelationalSqlRf3Tokens.SecondId : RelationalSqlRf3Tokens.InvalidId;
        var invalidRow = uniqueConflict ? RelationalSqlRf3Tokens.DuplicateTitleRow : RelationalSqlRf3Tokens.InvalidTypeRow;
        var command = scenario.Command(
            new PutDocument(RelationalSqlRf3Tokens.Documents, RelationalSqlRf3Tokens.RolledBackId, RelationalSqlRf3Tokens.EmptyJson),
            new AppendEvents(RelationalSqlRf3Tokens.Streams, RelationalSqlRf3Tokens.RolledBackId,
                [new(RelationalSqlRf3Tokens.EventId, RelationalSqlRf3Tokens.EventType, RelationalSqlRf3Tokens.EmptyJson)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(RelationalSqlRf3Tokens.Queue, RelationalSqlRf3Tokens.RolledBackId, RelationalSqlRf3Tokens.EmptyJson),
            new PutDocument(RelationalSqlRf3Tokens.Table, invalidId, invalidRow));
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var error = uniqueConflict ? ErrorCode.Conflict : ErrorCode.Validation;
        var result = await sdk.ExecuteSqlAsync(sql, deadline.Token);
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(error.ToString());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, deadline.Token), error, dispatched: true);
        await VerifyRollbackAsync(sdk, mcp, scenario, invalidId, deadline.Token);
        await SqlRf3Protocol.EqualAsync(before, await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, deadline.Token)));
    }

    private static async Task VerifyRollbackAsync(KeyLoadClient sdk, McpOfficialClient mcp, RelationalSqlRf3Scenario scenario,
        string invalidId, CancellationToken cancellationToken)
    {
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(
            new(scenario.Partition, RelationalSqlRf3Tokens.Documents, RelationalSqlRf3Tokens.RolledBackId), cancellationToken))).IsNull();
        var invalid = new GetDocumentRequest(new(scenario.Partition, RelationalSqlRf3Tokens.Table, invalidId));
        await Assert.That(await SqlRf3Protocol.McpAsync<DocumentResult?>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsGet, invalid), cancellationToken)).IsNull();
        var queue = scenario.Inspect(RelationalSqlRf3Tokens.RolledBackId);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(queue, cancellationToken))).IsNull();
        await Assert.That(await SqlRf3Protocol.McpAsync<MessageInspection?>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, queue), cancellationToken)).IsNull();
        var request = scenario.ReadStream(RelationalSqlRf3Tokens.RolledBackId);
        var stream = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(request, cancellationToken));
        var official = await SqlRf3Protocol.McpAsync<StreamPage>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request), cancellationToken);
        await Assert.That(stream.Events).IsEmpty();
        await Assert.That(official.Events).IsEmpty();
        await Assert.That(stream.Head.TailRevision).IsEqualTo(RelationalSqlRf3Tokens.InitialEventRevision);
        await Assert.That(official.Head.TailRevision).IsEqualTo(RelationalSqlRf3Tokens.InitialEventRevision);
    }
}
