using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlFetchCountClusterCancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlFetchCountClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlFetchCountClusterAssertions.ImageAsync(root, state, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var cancelled = new CancellationTokenSource();
                    await cancelled.CancelAsync();
                    var operation = SqlFetchCountClusterProtocol.Operation(SqlFetchCountClusterProtocol.FirstOne);
                    await SqlClusterProtocol.SdkCancelledAsync(await caller.Sdk.ExecuteSqlAsync(operation, cancelled.Token));
                    var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(cancelled.Token);
                    var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => wire.ExecuteAsync<QueryPage>(SqlFetchCountClusterProtocol.FirstOne, [], prepared: false, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(cancelled.Token);
                    await global::TUnit.Assertions.Assert.That(await SqlFetchCountClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
                    await SqlFetchCountClusterPages.CaseAsync(caller, wire, SqlFetchCountClusterProtocol.FirstOne, 1, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlFetchCountClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
    }
}
