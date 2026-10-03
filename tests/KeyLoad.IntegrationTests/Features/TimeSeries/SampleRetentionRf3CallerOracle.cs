using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleRetentionRf3CallerOracle
{
    private const int WindowCount = 4;
    private const int WindowMinutes = 1;

    internal static async Task WaitForNodeAsync(KeyLoadClient client, TimeSeriesRf3Scenario scenario,
        SampleRetentionStatus expected, string[] expectedIds, CancellationToken cancellationToken)
    {
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var progress = await client.ReadSampleRetentionAsync(RetentionRequest(scenario), cancellationToken);
            if (!progress.IsSuccess || progress.Value != expected)
            {
                return false;
            }
            var range = await client.ReadSamplesAsync(ReadRequest(scenario), cancellationToken);
            return range.IsSuccess && range.Value!.Select(row => row.Sample.EventId)
                .SequenceEqual(expectedIds, StringComparer.Ordinal);
        }, cancellationToken);
    }

    internal static async Task VerifyClientAsync(KeyLoadClient client, TimeSeriesRf3Scenario scenario,
        SampleRetentionStatus status, string[] expectedIds, CancellationToken cancellationToken)
    {
        var progress = ClusterReplicationTestSupport.Success(await client.ReadSampleRetentionAsync(
            RetentionRequest(scenario), cancellationToken));
        await Assert.That(progress).IsEqualTo(status);
        var range = ClusterReplicationTestSupport.Success(await client.ReadSamplesAsync(
            ReadRequest(scenario), cancellationToken));
        await Assert.That(range.Select(row => row.Sample.EventId))
            .IsEquivalentTo(expectedIds, CollectionOrdering.Matching);
        await VerifyLatestAsync(client, scenario, expectedIds[^1], cancellationToken);
        await VerifyAggregateAsync(client, scenario, expectedIds, cancellationToken);
        await VerifyWindowsAsync(client, scenario, expectedIds, cancellationToken);
    }

    internal static async Task VerifyMcpAsync(McpOfficialClient mcp, TimeSeriesRf3Scenario scenario,
        SampleRetentionStatus status, string[] expectedIds, CancellationToken cancellationToken)
    {
        var progress = await McpCallerAssertions.SuccessAsync<SampleRetentionStatus>(
            await mcp.CallAsync(McpCallerTools.SeriesRetention, RetentionRequest(scenario), cancellationToken));
        await Assert.That(progress.Value).IsEqualTo(status);
        var range = await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await mcp.CallAsync(McpCallerTools.SeriesRead, ReadRequest(scenario), cancellationToken));
        await Assert.That(range.Value.Select(row => row.Sample.EventId))
            .IsEquivalentTo(expectedIds, CollectionOrdering.Matching);
        await VerifyMcpLatestAsync(mcp, scenario, expectedIds[^1], cancellationToken);
        await VerifyMcpAggregateAsync(mcp, scenario, expectedIds, cancellationToken);
        await VerifyMcpWindowsAsync(mcp, scenario, expectedIds, cancellationToken);
    }

    private static async Task VerifyLatestAsync(KeyLoadClient client, TimeSeriesRf3Scenario scenario,
        string expectedId, CancellationToken cancellationToken)
    {
        var latest = ClusterReplicationTestSupport.Success(await client.ReadLatestSampleAsync(
            scenario.Latest(), cancellationToken));
        await Assert.That(latest.Sample?.Sample.EventId).IsEqualTo(expectedId);
    }

    private static async Task VerifyAggregateAsync(KeyLoadClient client, TimeSeriesRf3Scenario scenario,
        string[] expectedIds, CancellationToken cancellationToken)
    {
        var request = scenario.Aggregate(TimeSeriesRf3Scenario.Start,
            TimeSeriesRf3Scenario.Start.AddMinutes(WindowCount));
        var aggregate = ClusterReplicationTestSupport.Success(await client.AggregateSamplesAsync(request, cancellationToken));
        await Assert.That(aggregate).IsEqualTo(RawOracle(expectedIds));
    }

    private static async Task VerifyWindowsAsync(KeyLoadClient client, TimeSeriesRf3Scenario scenario,
        string[] expectedIds, CancellationToken cancellationToken)
    {
        var windows = ClusterReplicationTestSupport.Success(await client.AggregateSampleWindowsAsync(
            WindowsRequest(scenario), cancellationToken));
        await VerifyWindowsOracleAsync(windows, expectedIds);
    }

    private static async Task VerifyMcpLatestAsync(McpOfficialClient mcp, TimeSeriesRf3Scenario scenario,
        string expectedId, CancellationToken cancellationToken)
    {
        var latest = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await mcp.CallAsync(McpCallerTools.SeriesLatest, scenario.Latest(), cancellationToken));
        await Assert.That(latest.Value.Sample?.Sample.EventId).IsEqualTo(expectedId);
    }

    private static async Task VerifyMcpAggregateAsync(McpOfficialClient mcp, TimeSeriesRf3Scenario scenario,
        string[] expectedIds, CancellationToken cancellationToken)
    {
        var request = scenario.Aggregate(TimeSeriesRf3Scenario.Start,
            TimeSeriesRf3Scenario.Start.AddMinutes(WindowCount));
        var aggregate = await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await mcp.CallAsync(McpCallerTools.SeriesAggregate, request, cancellationToken));
        await Assert.That(aggregate.Value).IsEqualTo(RawOracle(expectedIds));
    }

    private static async Task VerifyMcpWindowsAsync(McpOfficialClient mcp, TimeSeriesRf3Scenario scenario,
        string[] expectedIds, CancellationToken cancellationToken)
    {
        var windows = await McpCallerAssertions.SuccessAsync<SampleAggregateWindowsResult>(
            await mcp.CallAsync(McpCallerTools.SeriesWindows, WindowsRequest(scenario), cancellationToken));
        await VerifyWindowsOracleAsync(windows.Value, expectedIds);
    }

    private static async Task VerifyWindowsOracleAsync(SampleAggregateWindowsResult actual, string[] expectedIds)
    {
        var expected = Enumerable.Range(0, WindowCount).Select(index =>
        {
            var values = expectedIds.Select(id => (Index: SampleIndex(id), Value: SampleValue(id)))
                .Where(sample => sample.Index == index).ToArray();
            return new SampleAggregate(values.Length, values.Sum(sample => sample.Value),
                values.Length == 0 ? null : values.Min(sample => sample.Value),
                values.Length == 0 ? null : values.Max(sample => sample.Value),
                values.Length == 0 ? null : values.Sum(sample => sample.Value) / values.Length);
        }).ToArray();
        await Assert.That(actual.Windows.Length).IsEqualTo(WindowCount);
        for (var index = 0; index < WindowCount; index++)
        {
            await Assert.That(actual.Windows[index].From)
                .IsEqualTo(TimeSeriesRf3Scenario.Start.AddMinutes(index));
            await Assert.That(actual.Windows[index].Aggregate).IsEqualTo(expected[index]);
        }
    }

    private static SampleAggregate RawOracle(string[] expectedIds)
    {
        var values = expectedIds.Select(SampleValue).ToArray();
        if (values.Length == 0)
        {
            return new(0, 0, null, null, null);
        }
        var sum = values.Aggregate(0d, static (current, value) => current + value);
        return new(values.LongLength, sum, values.Min(), values.Max(), sum / values.LongLength);
    }

    private static int ParseIndex(string id) => int.Parse(id.AsSpan("retention-".Length),
        System.Globalization.CultureInfo.InvariantCulture);

    private static double ValueFor(int index) => index switch { 0 => 1, 1 => 2, 2 => 4, 3 => 8, _ => 0 };

    private static int SampleIndex(string id) => id == "at-retention-floor" ? 3 : ParseIndex(id);

    private static double SampleValue(string id) => id == "at-retention-floor" ? 16 : ValueFor(ParseIndex(id));

    private static ReadSampleRetentionRequest RetentionRequest(TimeSeriesRf3Scenario scenario)
        => new(scenario.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series);

    private static ReadSamplesRequest ReadRequest(TimeSeriesRf3Scenario scenario)
        => new(scenario.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
            TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start.AddMinutes(WindowCount), 10);

    private static AggregateSampleWindowsRequest WindowsRequest(TimeSeriesRf3Scenario scenario)
        => scenario.Windows(TimeSeriesRf3Scenario.Start, TimeSeriesRf3Scenario.Start.AddMinutes(WindowCount),
            TimeSpan.FromMinutes(WindowMinutes));
}
