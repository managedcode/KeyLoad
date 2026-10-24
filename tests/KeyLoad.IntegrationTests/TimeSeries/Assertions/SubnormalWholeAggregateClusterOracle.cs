using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SubnormalWholeAggregateClusterOracle
{
    internal const long SealedCount = 2;
    internal const long CorrectedCount = 3;
    private const long HealthyCount = 1;

    internal static async Task PairAsync(SampleChunkClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, long count, Func<Task> current, CancellationToken token)
        => await ReadAsync(scenario, sdk, mcp, TimeSeriesClusterScenario.Series,
            new(count, 0d, -double.Epsilon, double.Epsilon, 0d), current, token);

    internal static async Task HealthyAsync(SampleChunkClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, Func<Task> current, CancellationToken token)
        => await ReadAsync(scenario, sdk, mcp, TimeSeriesClusterScenario.EmptySeries,
            new(HealthyCount, double.Epsilon, double.Epsilon, double.Epsilon, double.Epsilon), current, token);

    private static async Task ReadAsync(SampleChunkClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, string series, SampleAggregate expected, Func<Task> current,
        CancellationToken token)
    {
        var request = new AggregateSamplesRequest(scenario.Execution.Partition, TimeSeriesClusterScenario.Set,
            series, scenario.From, scenario.Until, SampleChunkClusterProtocol.OutputLimit);
        await EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await sdk.AggregateSamplesAsync(request, token).ConfigureAwait(false)));
        await current();
        await EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await mcp.CallAsync(McpCallerTools.SeriesAggregate, request, token).ConfigureAwait(false))).Value);
        await current();
    }

    private static async Task EqualAsync(SampleAggregate expected, SampleAggregate actual)
    {
        await SampleChunkRetentionClusterReadOracle.EqualAsync(expected, actual);
        await Assert.That(BitConverter.DoubleToInt64Bits(actual.Sum)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected.Sum));
        await Assert.That(BitConverter.DoubleToInt64Bits(actual.Minimum!.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected.Minimum!.Value));
        await Assert.That(BitConverter.DoubleToInt64Bits(actual.Maximum!.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected.Maximum!.Value));
        await Assert.That(BitConverter.DoubleToInt64Bits(actual.Average!.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected.Average!.Value));
    }
}
