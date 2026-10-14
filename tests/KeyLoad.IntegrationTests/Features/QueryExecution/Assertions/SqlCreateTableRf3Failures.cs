using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Npgsql;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Failures
{
    internal static async Task SdkAsync(KeyLoadClient sdk, SqlOperationRequest request, ErrorCode expected, CancellationToken token)
    {
        var result = await sdk.ExecuteSqlAsync(request, token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem).IsNotNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }
    internal static async Task McpAsync(McpOfficialClient caller, SqlOperationRequest request,
        ErrorCode expected, bool dispatched, CancellationToken token)
        => await McpCallerAssertions.ErrorAsync(await caller.CallAsync(SqlOperationProtocol.ToolName, request, token), expected, dispatched);
    internal static async Task WireAsync(PostgresRf3Caller caller, string sql, string expectedState, CancellationToken token)
    {
        PostgresException? refusal = null;
        try
        { await caller.ExecuteAsync<ResourceDefinition>(sql, [], prepared: true, token); }
        catch (PostgresException failure) { refusal = failure; }
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!.SqlState).IsEqualTo(expectedState);
    }
    internal static async Task WireCommandAsync(PostgresRf3Caller caller, CommandRequest command, CancellationToken token)
        => await WireCommandAsync(caller, command, SqlCreateTableRf3Literal.ValidationState, token);
    internal static async Task WireCommandAsync(PostgresRf3Caller caller, CommandRequest command, string expected, CancellationToken token)
    {
        PostgresException? refusal = null;
        try
        { await caller.CommitAsync(command, prepared: true, token); }
        catch (PostgresException failure) { refusal = failure; }
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!.SqlState).IsEqualTo(expected);
    }
}
