using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class SampleChunkRetainedIdentityClusterFlow
{
    internal static async Task<SampleChunkRetainedIdentityClusterState> RefuseAsync(SampleChunkClusterScenario scenario,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var request = SampleChunkRetentionClusterSeed.Request(scenario, new OpenSampleChunkWindow(TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, scenario.WindowId, scenario.Until,
            scenario.Until.AddHours(SampleChunkClusterProtocol.WindowHours)));
        var original = await CaptureAsync(request, sdk, mcp, token);
        await SampleChunkRetainedIdentityClusterAssertions.RequireAsync(sdk, mcp, original,
            () => EmptyAsync(scenario, sdk, mcp, token), token);
        return original;
    }

    internal static async Task<SampleChunkRetainedIdentityClusterState> CaptureAsync(CommandRequest request,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(request.Partition, token));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(request.Partition), token))).Value);
        return new(request, expected);
    }

    internal static Task HealthyAsync(SampleChunkRetentionClusterContinuation original, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
        => SampleChunkRetainedIdentityClusterAssertions.RequireAsync(sdk, mcp, original.RetainedIdentity,
            () => SampleChunkRetentionClusterHealthy.RequireAsync(original, sdk, mcp, token), token);

    private static async Task EmptyAsync(SampleChunkClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => SampleChunkRetentionClusterDenialOracle.WindowAsync(sdk, mcp,
            scenario.Request, token), failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkRetentionClusterReadOracle.RawAsync(sdk, mcp,
            new(scenario.Execution.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, scenario.From,
                scenario.Until.AddHours(SampleChunkClusterProtocol.WindowHours), SampleChunkClusterProtocol.OutputLimit), [], token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
