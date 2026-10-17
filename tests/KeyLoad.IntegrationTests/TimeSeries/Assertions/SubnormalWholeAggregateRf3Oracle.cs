using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SubnormalWholeAggregateRf3Oracle
{
    internal const long SealedCount = 2;
    internal const long CorrectedCount = 3;
    private const long HealthyCount = 1;

    internal static async Task PairAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, long count, Func<Task> current, CancellationToken token)
        => await ReadAsync(scenario, sdk, mcp, TimeSeriesRf3Scenario.Series,
            new(count, 0d, -double.Epsilon, double.Epsilon, 0d), current, token);

    internal static async Task HealthyAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, Func<Task> current, CancellationToken token)
        => await ReadAsync(scenario, sdk, mcp, TimeSeriesRf3Scenario.EmptySeries,
            new(HealthyCount, double.Epsilon, double.Epsilon, double.Epsilon, double.Epsilon), current, token);

    private static async Task ReadAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, string series, SampleAggregate expected, Func<Task> current,
        CancellationToken token)
    {
        var request = new AggregateSamplesRequest(scenario.Native.Partition, TimeSeriesRf3Scenario.Set,
            series, scenario.From, scenario.Until, SampleChunkRf3Protocol.OutputLimit);
        await EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await sdk.AggregateSamplesAsync(request, token).ConfigureAwait(false)));
        await current();
        await EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await mcp.CallAsync(McpCallerTools.SeriesAggregate, request, token).ConfigureAwait(false))).Value);
        await current();
    }

    private static async Task EqualAsync(SampleAggregate expected, SampleAggregate actual)
    {
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(expected, actual);
        await Assert.That(BitConverter.DoubleToInt64Bits(actual.Sum)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected.Sum));
        await Assert.That(BitConverter.DoubleToInt64Bits(actual.Minimum!.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected.Minimum!.Value));
        await Assert.That(BitConverter.DoubleToInt64Bits(actual.Maximum!.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected.Maximum!.Value));
        await Assert.That(BitConverter.DoubleToInt64Bits(actual.Average!.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected.Average!.Value));
    }
}
