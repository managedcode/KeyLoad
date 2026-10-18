using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeTextWaitClusterTests(ClusterFixture fixture)
{
    private const string Node = McpCallerProtocol.Node1;
    private const string PermissionCode = nameof(ErrorCode.PermissionDenied);
    private const string PermissionDetail = "The query requires a protected field-use grant.";
    private const long OriginalRevision = 1;

    [Test]
    public async Task AcknowledgedNativeTextPrefixSdkOfficialMcpAndQ1CallRejectDeniedThenPublishHealthyAndExcludeDeleted()
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var scenario = await NativeTextClusterScenario.CreateAsync(fixture, token);
        var deniedIdentity = await scenario.CreateReaderAsync(fixture, false, true, false, false, token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, false, token);
        using var deniedHttp = McpCallerHttp.Create(fixture, Node);
        using var callerHttp = McpCallerHttp.Create(fixture, Node);
        using var adminHttp = McpCallerHttp.Create(fixture, Node);
        var denied = new KeyLoadClient(deniedHttp, deniedIdentity.Secret, IntegrationClientOptions.Execution());
        var caller = new KeyLoadClient(callerHttp, identity.Secret, IntegrationClientOptions.Execution());
        var administrator = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var deniedMcp = await McpOfficialClient.ConnectAsync(fixture, Node, deniedIdentity.Secret, token);
        await using var official = await McpOfficialClient.ConnectAsync(fixture, Node, identity.Secret, token);
        var request = new WaitForIndexRequest(scenario.Partition, NativeTextClusterScenario.Collection,
            NativeTextClusterScenario.TextField, scenario.SeedToken);
        var cut = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        var history = await NativeTextClusterReplayHistory.CaptureAsync(administrator, scenario.Partition, token);
        await NativeTextClusterReplayHistory.RequireSeedAsync(scenario, history);
        await NativeTextClusterReplayHistory.RequireDocumentsAsync(administrator, scenario, token);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.OutboxStatusAsync(scenario.Partition, token));
        await NativeTextClusterReplayHistory.RequireSeedOutboxAsync(outbox);
        var failure = await denied.WaitForIndexAsync(request, token);
        await Assert.That(failure.IsSuccess).IsFalse();
        await Assert.That(failure.Value).IsNull();
        var problem = failure.Problem ?? throw new InvalidOperationException(PermissionCode);
        await Assert.That(problem.ErrorCode).IsEqualTo(PermissionCode);
        await Assert.That(problem.Detail).IsEqualTo(PermissionDetail);
        await McpCallerAssertions.ErrorAsync(await deniedMcp.CallAsync(WaitForIndexProtocol.Tool, request, token),
            ErrorCode.PermissionDenied, dispatched: true);
        var sql = SqlClusterProtocol.Call(request.Partition, WaitForIndexProtocol.Tool, request);
        var sqlDenied = await denied.ExecuteSqlAsync(sql, token);
        await Assert.That(sqlDenied.IsSuccess).IsFalse();
        await Assert.That(sqlDenied.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
        await Assert.That(sqlDenied.Problem?.ErrorCode).IsEqualTo(PermissionCode);
        await Assert.That(sqlDenied.Problem?.Detail).IsEqualTo(PermissionDetail);
        await McpCallerAssertions.ErrorAsync(await deniedMcp.CallAsync(SqlOperationProtocol.ToolName, sql, token),
            ErrorCode.PermissionDenied, dispatched: true);
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        await NativeTextWaitClusterCutAssertions.OwnerAsync(cut, after);
        await Assert.That(after.Position).IsGreaterThanOrEqualTo(cut.Position);
        await NativeTextClusterReplayHistory.RequireAsync(administrator, scenario.Partition, history, token);
        await NativeTextClusterReplayHistory.RequireDocumentsAsync(administrator, scenario, token);
        await SqlClusterProtocol.EqualAsync(outbox, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.OutboxStatusAsync(scenario.Partition, token)));
        await NativeTextWaitClusterAssertions.WaitAsync(caller, official, administrator, request,
            scenario.Resource.SchemaVersion, identity.Principal.PolicyEpoch, token);
        await NativeTextWaitClusterAssertions.LiteralAsync(scenario, caller, official, false, token);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new DeleteDocument(NativeTextClusterScenario.Collection, NativeTextClusterScenario.SecondId, OriginalRevision)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, token));
        await NativeTextWaitClusterAssertions.WaitAsync(caller, official, administrator, request with
        {
            MinimumToken = receipt.Token
        },
            scenario.Resource.SchemaVersion, identity.Principal.PolicyEpoch, token);
        await NativeTextWaitClusterAssertions.LiteralAsync(scenario, caller, official, true, token);
        var deleted = await McpCallerAssertions.SdkSuccessAsync(await administrator.GetAsync(
            new(scenario.Partition, NativeTextClusterScenario.Collection, NativeTextClusterScenario.SecondId), token));
        await Assert.That(deleted.Document).IsNull();
    }
}
