using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochRf3ExpectedSample(SampleData Sample, long Sequence, string Tags);

internal static class NodeEpochRf3ReadOracle
{
    private const int WindowMinutes = 10;
    private const int WindowCount = 7;
    private const string ReadStartDocument = "{\"generation\":1,\"source\":\"native5\"}";

    internal static ImmutableArray<NodeEpochRf3ExpectedSample> Prior(NodeEpochRf3Workload workload)
        => workload.PriorSamples.Select((sample, index) => new NodeEpochRf3ExpectedSample(sample, index + 1,
            NodeEpochRf3Workload.Tags(index))).ToImmutableArray();

    internal static async Task VerifyFullAsync(NodeEpochRf3Callers callers, NodeEpochRf3Workload workload,
        ImmutableArray<NodeEpochRf3ExpectedSample> expected, CancellationToken cancellationToken)
    {
        await VerifyDocumentAsync(callers, workload, expectedCurrent: true, cancellationToken).ConfigureAwait(false);
        await VerifySeriesAsync(callers, workload, expected, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task VerifySeriesAsync(NodeEpochRf3Callers callers, NodeEpochRf3Workload workload,
        ImmutableArray<NodeEpochRf3ExpectedSample> expected, CancellationToken cancellationToken)
    {
        var rangeRequest = new ReadSamplesRequest(workload.Partition, workload.SeriesSet, workload.SeriesId,
            NodeEpochRf3Protocol.SampleStart, NodeEpochRf3Protocol.SampleStart.AddMinutes(70), 100);
        var sdkRange = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSamplesAsync(rangeRequest,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcpRange = await McpCallerAssertions.SuccessAsync<SampleRecord[]>(await callers.Mcp.CallAsync(
            McpCallerTools.SeriesRead, rangeRequest, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await AssertSamplesAsync(sdkRange, expected).ConfigureAwait(false);
        await AssertSamplesAsync(mcpRange.Value, expected).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(sdkRange).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcpRange.Value))).IsTrue();
        await VerifyLatestAsync(callers, workload, expected, cancellationToken).ConfigureAwait(false);
        await VerifyAggregateAsync(callers, workload, expected, cancellationToken).ConfigureAwait(false);
        await VerifyWindowsAsync(callers, workload, expected, cancellationToken).ConfigureAwait(false);
    }

    internal static ImmutableArray<NodeEpochRf3ExpectedSample> RetainedAfterFloor(NodeEpochRf3Workload workload)
    {
        var retained = Prior(workload).Where(row => row.Sample.Timestamp.UtcTicks >= NodeEpochRf3Protocol.SampleStart.AddMinutes(10).UtcTicks)
            .ToList();
        retained.Add(new(new("at-retention-floor", NodeEpochRf3Protocol.SampleStart.AddMinutes(10), 25), 25,
            "{\"origin\":\"native6\"}"));
        return [.. retained.OrderBy(row => row.Sample.Timestamp.UtcTicks).ThenBy(row => row.Sequence)];
    }

    internal static async Task VerifyRetentionStatusAsync(NodeEpochRf3Callers callers, NodeEpochRf3Workload workload,
        DateTimeOffset? before, long purgedCount, bool hasMore, CancellationToken cancellationToken)
    {
        var request = new ReadSampleRetentionRequest(workload.Partition, workload.SeriesSet, workload.SeriesId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSampleRetentionAsync(request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<SampleRetentionStatus>(await callers.Mcp.CallAsync(
            McpCallerTools.SeriesRetention, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var expected = new SampleRetentionStatus(before, purgedCount, hasMore);
        await Assert.That(sdk).IsEqualTo(expected);
        await Assert.That(mcp.Value).IsEqualTo(expected);
    }

    internal static async Task VerifyDocumentAsync(NodeEpochRf3Callers callers, NodeEpochRf3Workload workload,
        bool expectedCurrent, CancellationToken cancellationToken)
    {
        var reference = new EntityRef(workload.Partition, workload.Collection, workload.DocumentId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetAsync(reference,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        var expectedJson = expectedCurrent ? NodeEpochRf3Workload.DocumentJson : ReadStartDocument;
        await Assert.That(sdk?.Revision).IsEqualTo(expectedCurrent ? 2L : 1L);
        await Assert.That(mcp.Value?.Revision).IsEqualTo(expectedCurrent ? 2L : 1L);
        await Assert.That(sdk?.Json).IsEqualTo(expectedJson);
        await Assert.That(mcp.Value?.Json).IsEqualTo(expectedJson);
    }

    private static async Task VerifyLatestAsync(NodeEpochRf3Callers callers, NodeEpochRf3Workload workload,
        ImmutableArray<NodeEpochRf3ExpectedSample> expected, CancellationToken cancellationToken)
    {
        var request = new ReadLatestSampleRequest(workload.Partition, workload.SeriesSet, workload.SeriesId,
            NodeEpochRf3Protocol.SampleStart.AddMinutes(70));
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadLatestSampleAsync(request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(await callers.Mcp.CallAsync(
            McpCallerTools.SeriesLatest, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var last = expected.IsEmpty ? null : expected[^1];
        await AssertLatestSampleAsync(sdk.Sample, last).ConfigureAwait(false);
        await AssertLatestSampleAsync(mcp.Value.Sample, last).ConfigureAwait(false);
    }

    private static async Task AssertLatestSampleAsync(SampleRecord? actual, NodeEpochRf3ExpectedSample? expected)
    {
        if (expected is null)
        {
            await Assert.That(actual).IsNull();
            return;
        }
        await AssertSamplesAsync([actual ?? throw new InvalidOperationException("Expected latest sample was absent.")],
            [expected]).ConfigureAwait(false);
    }

    private static async Task VerifyAggregateAsync(NodeEpochRf3Callers callers, NodeEpochRf3Workload workload,
        ImmutableArray<NodeEpochRf3ExpectedSample> expected, CancellationToken cancellationToken)
    {
        var request = new AggregateSamplesRequest(workload.Partition, workload.SeriesSet, workload.SeriesId,
            NodeEpochRf3Protocol.SampleStart, NodeEpochRf3Protocol.SampleStart.AddMinutes(70), 100);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AggregateSamplesAsync(request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<SampleAggregate>(await callers.Mcp.CallAsync(
            McpCallerTools.SeriesAggregate, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var values = expected.Select(row => row.Sample.Value).ToArray();
        var sum = values.Aggregate(0d, static (current, value) => current + value);
        var oracle = values.Length == 0 ? new SampleAggregate(0, 0, null, null, null)
            : new(values.LongLength, sum, values.Min(), values.Max(), sum / values.LongLength);
        await Assert.That(sdk).IsEqualTo(oracle);
        await Assert.That(mcp.Value).IsEqualTo(oracle);
    }

    private static async Task VerifyWindowsAsync(NodeEpochRf3Callers callers, NodeEpochRf3Workload workload,
        ImmutableArray<NodeEpochRf3ExpectedSample> expected, CancellationToken cancellationToken)
    {
        var start = NodeEpochRf3Protocol.SampleStart;
        var until = start.AddMinutes(WindowMinutes * WindowCount);
        var request = new AggregateSampleWindowsRequest(workload.Partition, workload.SeriesSet, workload.SeriesId,
            start.ToOffset(TimeSpan.FromHours(3)), until.ToOffset(TimeSpan.FromHours(-4)), TimeSpan.FromMinutes(WindowMinutes), 100, WindowCount);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AggregateSampleWindowsAsync(request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<SampleAggregateWindowsResult>(await callers.Mcp.CallAsync(
            McpCallerTools.SeriesWindows, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(sdk.Windows.Length).IsEqualTo(WindowCount);
        await Assert.That(mcp.Value.Windows.Length).IsEqualTo(WindowCount);
        for (var index = 0; index < WindowCount; index++)
        {
            var from = start.AddMinutes(index * WindowMinutes);
            var untilExclusive = from.AddMinutes(WindowMinutes);
            var rows = expected.Where(row => row.Sample.Timestamp.UtcTicks >= from.UtcTicks
                && row.Sample.Timestamp.UtcTicks < untilExclusive.UtcTicks).ToArray();
            var values = rows.Select(row => row.Sample.Value).ToArray();
            var sum = values.Aggregate(0d, static (current, value) => current + value);
            var aggregate = values.Length == 0 ? new SampleAggregate(0, 0, null, null, null)
                : new(values.LongLength, sum, values.Min(), values.Max(), sum / values.LongLength);
            await Assert.That(sdk.Windows[index].From.UtcTicks).IsEqualTo(from.UtcTicks);
            await Assert.That(sdk.Windows[index].From.Offset).IsEqualTo(TimeSpan.Zero);
            await Assert.That(sdk.Windows[index].UntilExclusive?.UtcTicks).IsEqualTo(untilExclusive.UtcTicks);
            await Assert.That(sdk.Windows[index].UntilExclusive?.Offset).IsEqualTo(TimeSpan.Zero);
            await Assert.That(sdk.Windows[index].Aggregate).IsEqualTo(aggregate);
            await Assert.That(mcp.Value.Windows[index]).IsEqualTo(sdk.Windows[index]);
        }
        await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value))).IsTrue();
    }

    internal static async Task AssertSamplesAsync(SampleRecord[] actual,
        ImmutableArray<NodeEpochRf3ExpectedSample> expected)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var row = actual[index];
            var oracle = expected[index];
            await Assert.That(row.SeriesId).IsEqualTo(NodeEpochRf3Workload.SeriesName);
            await Assert.That(row.Sample.EventId).IsEqualTo(oracle.Sample.EventId);
            await Assert.That(row.Sample.Timestamp.UtcTicks).IsEqualTo(oracle.Sample.Timestamp.UtcTicks);
            await Assert.That(row.Sample.Timestamp.Offset).IsEqualTo(oracle.Sample.Timestamp.Offset);
            await Assert.That(BitConverter.DoubleToInt64Bits(row.Sample.Value))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(oracle.Sample.Value));
            await Assert.That(row.Sequence).IsEqualTo(oracle.Sequence);
            await Assert.That(row.TagsJson).IsEqualTo(oracle.Tags);
        }
    }
}
