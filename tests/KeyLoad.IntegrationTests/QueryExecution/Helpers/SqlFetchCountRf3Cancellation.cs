using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlFetchCountRf3Cancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlFetchCountRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlFetchCountRf3Assertions.ImageAsync(root, state, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var cancelled = new CancellationTokenSource();
                    await cancelled.CancelAsync();
                    var operation = SqlFetchCountRf3Protocol.Operation(SqlFetchCountRf3Protocol.FirstOne);
                    var sdk = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => caller.Sdk.ExecuteSqlAsync(operation, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(sdk?.CancellationToken).IsEqualTo(cancelled.Token);
                    var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(cancelled.Token);
                    var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => wire.ExecuteAsync<QueryPage>(SqlFetchCountRf3Protocol.FirstOne, [], prepared: false, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(cancelled.Token);
                    await global::TUnit.Assertions.Assert.That(await SqlFetchCountRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
                    await SqlFetchCountRf3Pages.CaseAsync(caller, wire, SqlFetchCountRf3Protocol.FirstOne, 1, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlFetchCountRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
    }
}
