using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetainedIdentityRf3Flow
{
    internal static async Task<SampleChunkRetainedIdentityRf3State> RefuseAsync(SampleChunkRf3Scenario scenario,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var request = SampleChunkRetentionRf3Seed.Request(scenario, new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, scenario.WindowId, scenario.Until,
            scenario.Until.AddHours(SampleChunkRf3Protocol.WindowHours)));
        var original = await CaptureAsync(request, sdk, mcp, token);
        await SampleChunkRetainedIdentityRf3Assertions.RequireAsync(sdk, mcp, original,
            () => EmptyAsync(scenario, sdk, mcp, token), token);
        return original;
    }

    internal static async Task<SampleChunkRetainedIdentityRf3State> CaptureAsync(CommandRequest request,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var expected = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(request.Partition, token));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(request.Partition), token))).Value);
        return new(request, expected);
    }

    internal static Task HealthyAsync(SampleChunkRetentionRf3Continuation original, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
        => SampleChunkRetainedIdentityRf3Assertions.RequireAsync(sdk, mcp, original.RetainedIdentity,
            () => SampleChunkRetentionRf3Healthy.RequireAsync(original, sdk, mcp, token), token);

    private static async Task EmptyAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => SampleChunkRetentionRf3DenialOracle.WindowAsync(sdk, mcp,
            scenario.Request, token), failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkRetentionRf3ReadOracle.RawAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, scenario.From,
                scenario.Until.AddHours(SampleChunkRf3Protocol.WindowHours), SampleChunkRf3Protocol.OutputLimit), [], token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
