using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeTextWaitRf3Tests(ClusterFixture fixture)
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
        var scenario = await NativeTextRf3Scenario.CreateAsync(fixture, token);
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
        var request = new WaitForIndexRequest(scenario.Partition, NativeTextRf3Scenario.Collection,
            NativeTextRf3Scenario.TextField, scenario.SeedToken);
        var cut = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        var failure = await denied.WaitForIndexAsync(request, token);
        await Assert.That(failure.IsSuccess).IsFalse();
        await Assert.That(failure.Value).IsNull();
        var problem = failure.Problem ?? throw new InvalidOperationException(PermissionCode);
        await Assert.That(problem.ErrorCode).IsEqualTo(PermissionCode);
        await Assert.That(problem.Detail).IsEqualTo(PermissionDetail);
        await McpCallerAssertions.ErrorAsync(await deniedMcp.CallAsync(WaitForIndexProtocol.Tool, request, token),
            ErrorCode.PermissionDenied, dispatched: true);
        var sql = SqlRf3Protocol.Call(request.Partition, WaitForIndexProtocol.Tool, request);
        var sqlDenied = await denied.ExecuteSqlAsync(sql, token);
        await Assert.That(sqlDenied.IsSuccess).IsFalse();
        await Assert.That(sqlDenied.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
        await Assert.That(sqlDenied.Problem?.ErrorCode).IsEqualTo(PermissionCode);
        await Assert.That(sqlDenied.Problem?.Detail).IsEqualTo(PermissionDetail);
        await McpCallerAssertions.ErrorAsync(await deniedMcp.CallAsync(SqlOperationProtocol.ToolName, sql, token),
            ErrorCode.PermissionDenied, dispatched: true);
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        await Assert.That(after.Applied).IsEqualTo(cut.Applied);
        await NativeTextWaitRf3Assertions.WaitAsync(caller, official, request, token);
        await NativeTextWaitRf3Assertions.LiteralAsync(scenario, caller, official, false, token);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new DeleteDocument(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.SecondId, OriginalRevision)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, token));
        await NativeTextWaitRf3Assertions.WaitAsync(caller, official, request with { MinimumToken = receipt.Token }, token);
        await NativeTextWaitRf3Assertions.LiteralAsync(scenario, caller, official, true, token);
        var deleted = await McpCallerAssertions.SdkSuccessAsync(await administrator.GetAsync(
            new(scenario.Partition, NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.SecondId), token));
        await Assert.That(deleted).IsNull();
    }
}
