using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelSourceArgumentRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk,
        SqlModelViewRf3Scenario scenario, CancellationToken token)
    {
        var original = await ModelSourceArgumentRf3Assertions.ImageAsync(sdk, scenario, token);
        for (var wave = 0; wave < Waves; wave++)
        {
            if (wave != 0)
            {
                await ModelSqlRf3Cold.RunAsync(fixture, token);
            }
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                    fixture.AdminKey, token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var (refused, healthy, events) in ModelSourceArgumentRf3Requests.All(scenario.Partition))
                    {
                        await RequireAsync(sdk, mcp, scenario, refused, healthy, events, token);
                    }
                }, failures);
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
                await Assert.That(await ModelSourceArgumentRf3Assertions.ImageAsync(sdk, scenario, token)).IsEqualTo(original), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlModelViewRf3Scenario scenario, QueryRequest refused, QueryRequest healthy, bool events, CancellationToken token)
    {
        await ModelSourceArgumentRf3Assertions.ObserveAsync(sdk, mcp, scenario, async () =>
        {
            var error = await sdk.QueryAsync(refused, token);
            await Assert.That(error.IsSuccess).IsFalse();
            await Assert.That(error.Value).IsNull();
            await Assert.That(error.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnsupportedCapability));
        }, token);
        await ModelSourceArgumentRf3Assertions.ObserveAsync(sdk, mcp, scenario, async () =>
            await ModelSourceArgumentRf3Assertions.PageAsync(
                await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(healthy, token)), events), token);
        await ModelSourceArgumentRf3Assertions.ObserveAsync(sdk, mcp, scenario, async () =>
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.QueryExecute, refused, token),
                ErrorCode.UnsupportedCapability, dispatched: true), token);
        await ModelSourceArgumentRf3Assertions.ObserveAsync(sdk, mcp, scenario, async () =>
            await ModelSourceArgumentRf3Assertions.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
                await mcp.CallAsync(McpCallerTools.QueryExecute, healthy, token))).Value, events), token);
    }
    private const int Waves = 3;
}
