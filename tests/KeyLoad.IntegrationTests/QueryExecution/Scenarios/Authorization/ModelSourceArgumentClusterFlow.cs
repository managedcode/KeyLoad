using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelSourceArgumentClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk,
        SqlModelViewClusterScenario scenario, CancellationToken token)
    {
        var original = await ModelSourceArgumentClusterAssertions.ImageAsync(sdk, scenario, token);
        for (var wave = 0; wave < Waves; wave++)
        {
            if (wave != 0)
            {
                await ModelSqlClusterCold.RunAsync(fixture, token);
            }
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                    fixture.AdminKey, token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var (refused, healthy, events) in ModelSourceArgumentClusterRequests.All(scenario.Partition))
                    {
                        await RequireAsync(sdk, mcp, scenario, refused, healthy, events, token);
                    }
                }, failures);
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
                await Assert.That(await ModelSourceArgumentClusterAssertions.ImageAsync(sdk, scenario, token)).IsEqualTo(original), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlModelViewClusterScenario scenario, QueryRequest refused, QueryRequest healthy, bool events, CancellationToken token)
    {
        await ModelSourceArgumentClusterAssertions.ObserveAsync(sdk, mcp, scenario, async () =>
        {
            var error = await sdk.QueryAsync(refused, token);
            await Assert.That(error.IsSuccess).IsFalse();
            await Assert.That(error.Value).IsNull();
            await Assert.That(error.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnsupportedCapability));
        }, token);
        await ModelSourceArgumentClusterAssertions.ObserveAsync(sdk, mcp, scenario, async () =>
            await ModelSourceArgumentClusterAssertions.PageAsync(
                await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(healthy, token)), events), token);
        await ModelSourceArgumentClusterAssertions.ObserveAsync(sdk, mcp, scenario, async () =>
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.QueryExecute, refused, token),
                ErrorCode.UnsupportedCapability, dispatched: true), token);
        await ModelSourceArgumentClusterAssertions.ObserveAsync(sdk, mcp, scenario, async () =>
            await ModelSourceArgumentClusterAssertions.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
                await mcp.CallAsync(McpCallerTools.QueryExecute, healthy, token))).Value, events), token);
    }
    private const int Waves = 3;
}
