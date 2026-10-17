using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries;

internal static class NativeConfiguredTimeSeriesReadRegression
{
    private const int ConfiguredReadLimit = 3;
    private const int CorpusCount = 48;
    private const long FirstStoredSequence = 1L;
    private const string PrimaryNode = "node1";
    private const string HttpEndpoint = "http";
    private const string RunIdFormat = "N";
    private const string FullRangeName = "inclusive-full-range";

    // AC-CQ-034: reuse the selected genuine RF3 resources and real SDK request path.
    internal static async Task VerifyAsync(DistributedApplication app, string adminKey, CancellationToken cancellationToken)
    {
        var execution = NativeExecutionPolicyFixture.Read();
        execution.Value.KeyLoadTimeSeriesReadLimit = ConfiguredReadLimit;
        execution.Value.Validate();
        var clientOptions = ComparisonClientOptions.Execution();
        using var http = app.CreateHttpClient(PrimaryNode, HttpEndpoint);
        await using var target = new KeyLoadTimeSeriesTarget(http, adminKey, clientOptions, execution);
        var workload = TimeSeriesComparisonWorkloadFactory.Create(Guid.NewGuid().ToString(RunIdFormat));
        var range = workload.ReadRanges.Single(item => item.Name == FullRangeName);
        await Assert.That(workload.Samples.Length).IsEqualTo(CorpusCount);
        await target.InitializeAsync(workload, cancellationToken);
        await target.SeedAsync(workload, cancellationToken);
        var sdk = new KeyLoadClient(http, adminKey, clientOptions);
        var fullRequest = new ReadSamplesRequest(workload.Partition, workload.SetName, workload.SeriesId,
            range.From, range.Until, workload.Samples.Length);
        var baseline = await sdk.ReadSamplesAsync(fullRequest, cancellationToken);
        await Assert.That(baseline.IsSuccess).IsTrue();
        await Assert.That(baseline.Value!.Length).IsEqualTo(CorpusCount);
        var expected = workload.Samples.OrderBy(sample => sample.Timestamp).ThenBy(sample => sample.Sequence).ToArray();
        await VerifyStoredRowsAsync(baseline.Value!, expected);
        await VerifyConfiguredReadAsync(target, workload, range, expected, cancellationToken);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cancelled.CancelAsync();
        var interrupted = await target.ReadAsync(workload, range, cancelled.Token);
        await Assert.That(interrupted.Succeeded).IsFalse();
        await Assert.That(interrupted.ErrorCode).IsEqualTo(ErrorCode.Cancelled.ToString());
        await Assert.That(interrupted.Samples.Length).IsEqualTo(0);
        await VerifyConfiguredReadAsync(target, workload, range, expected, cancellationToken);
        var healthy = await sdk.ReadSamplesAsync(fullRequest, cancellationToken);
        await Assert.That(healthy.IsSuccess).IsTrue();
        await Assert.That(healthy.Value!.Length).IsEqualTo(CorpusCount);
        await VerifyStoredRowsAsync(healthy.Value!, expected);
    }

    private static async Task VerifyConfiguredReadAsync(KeyLoadTimeSeriesTarget target, TimeSeriesComparisonWorkload workload,
        TimeSeriesReadRange range, TimeSeriesSamplePoint[] expected, CancellationToken cancellationToken)
    {
        var read = await target.ReadAsync(workload, range, cancellationToken);
        await Assert.That(read.Succeeded).IsTrue();
        await Assert.That(read.ErrorCode).IsNull();
        await Assert.That(read.Samples.Length).IsEqualTo(ConfiguredReadLimit);
        for (var index = 0; index < read.Samples.Length; index++)
        {
            var actual = read.Samples[index];
            var sample = expected[index];
            await Assert.That(actual.EventId).IsEqualTo(sample.EventId);
            await Assert.That(actual.Timestamp).IsEqualTo(sample.Timestamp);
            await Assert.That(actual.Value).IsEqualTo(sample.Value);
            await Assert.That(actual.Sequence).IsEqualTo(sample.Sequence + FirstStoredSequence);
            await Assert.That(actual.TagsJson).IsEqualTo(sample.TagsJson);
        }
    }

    private static async Task VerifyStoredRowsAsync(SampleRecord[] actual, TimeSeriesSamplePoint[] expected)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            var sample = expected[index];
            await Assert.That(actual[index].Sample.EventId).IsEqualTo(sample.EventId);
            await Assert.That(actual[index].Sample.Timestamp).IsEqualTo(sample.Timestamp);
            await Assert.That(actual[index].Sample.Value).IsEqualTo(sample.Value);
            await Assert.That(actual[index].Sequence).IsEqualTo(sample.Sequence + FirstStoredSequence);
            await Assert.That(actual[index].TagsJson).IsEqualTo(sample.TagsJson);
        }
    }
}
