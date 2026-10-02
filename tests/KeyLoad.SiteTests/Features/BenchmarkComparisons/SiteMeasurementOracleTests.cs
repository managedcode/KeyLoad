using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Compares production measurement rows with a separate C# oracle over authentic reports.</summary>
internal sealed class SiteMeasurementOracleTests
{
    /// <summary>Checks every scenario, metric, repetition, median row, output field, and row ordering.</summary>
    [Test]
    public async Task AC_BC_014_ProductionRowsMatchIndependentOracleForAllHistoricalInputs()
    {
        var inputs = SiteTestInputs.Read();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var jsonContext = SiteReportJsonContext.Create();
        foreach (var profile in SiteTokens.ProfileNames)
        {
            var path = Path.Combine(inputs.Reports, profile, SiteTokens.ReportFile);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var report = JsonSerializer.Deserialize(bytes, jsonContext.SiteReport) ?? throw new JsonException();
            await Assert.That(report.SourceRevision).IsEqualTo(inputs.MeasuredRevision);
            await Assert.That(report.Options.Repetitions > SiteTokens.Zero).IsTrue();
            await Assert.That(report.Targets.Count).IsEqualTo(SiteTokens.HistoricalTargetCount);
            await Assert.That(report.Cases.Select(item => item.Scenario).Distinct().Order()
                .SequenceEqual(SiteTokens.Scenarios.Order())).IsTrue();

            await VerifyProfileRows(inputs, report, path, cancellationToken);
        }
    }

    private static async Task VerifyProfileRows(SiteTestInputs inputs, SiteReport report, string path,
        CancellationToken cancellationToken)
    {
        foreach (var scenario in SiteTokens.Scenarios)
        {
            foreach (var metric in SiteTokens.Metrics)
            {
                for (var repetition = SiteTokens.Zero; repetition < report.Options.Repetitions; repetition++)
                {
                    await CompareRows(inputs, report, path, scenario, metric,
                        repetition.ToString(CultureInfo.InvariantCulture), cancellationToken);
                }

                await CompareRows(inputs, report, path, scenario, metric, SiteTokens.MedianRepetition, cancellationToken);
            }
        }
    }

    /// <summary>Preserves odd, even, and empty production median behavior through the Node probe.</summary>
    [Test]
    public async Task AC_BC_014_ProductionMedianPreservesOddEvenAndEmptyInputs()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var odd = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.MedianOperation, Values: SiteTokens.OddMedianValues), token);
        var even = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.MedianOperation, Values: SiteTokens.EvenMedianValues), token);
        var empty = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.MedianOperation, Values: []), token);
        await Assert.That(odd.Value.GetDouble()).IsEqualTo(5d);
        await Assert.That(even.Value.GetDouble()).IsEqualTo(5d);
        await Assert.That(empty.Value.ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    /// <summary>Treats captured stderr and a nonzero child-process exit as probe failures.</summary>
    [Test]
    public async Task AC_BC_017_ProbeTreatsNonzeroExitAndCapturedStderrAsFailure()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.InvalidOperation), token));
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure?.Message.Contains(SiteTokens.StandardErrorLabel, StringComparison.Ordinal) == true).IsTrue();
    }

    /// <summary>Honors caller cancellation while the bounded child process is starting.</summary>
    [Test]
    public async Task AC_BC_017_ProbeHonorsCallerCancellation()
    {
        var inputs = SiteTestInputs.Read();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.MedianOperation, Values: SiteTokens.CancellationMedianValues), cancellation.Token));
    }

    /// <summary>Checks failed attempts retain the denominator and malformed copies stay unpublishable.</summary>
    [Test]
    public async Task AC_BC_014_FailedAttemptsKeepTheFullDenominatorAndInvalidCopyIsNotPublishable()
    {
        var inputs = SiteTestInputs.Read();
        var original = await File.ReadAllTextAsync(Path.Combine(inputs.Reports, SiteTokens.SmokeProfile, SiteTokens.ReportFile));
        var edited = JsonNode.Parse(original)!.AsObject();
        var operations = edited[SiteTokens.Options]![SiteTokens.Operations]!.GetValue<int>();
        var cases = edited[SiteTokens.Cases]!.AsArray();
        var targetCase = cases.Single(item => item![SiteTokens.Target]!.GetValue<string>() == SiteTokens.KeyLoadTarget &&
            item[SiteTokens.Scenario]!.GetValue<string>() == SiteTokens.PointRead && item[SiteTokens.Repetition]!.GetValue<int>() == SiteTokens.Zero)!.AsObject();
        targetCase[SiteTokens.Status] = SiteTokens.FailedStatus;
        targetCase[SiteTokens.Measurement]![SiteTokens.Successes] = operations - SiteTokens.One;
        targetCase[SiteTokens.Measurement]![SiteTokens.Failures] = SiteTokens.One;
        var temporaryPath = Path.Combine(inputs.Reports, SiteTokens.SmokeProfile, $"{SiteTokens.ControlledInvalidPrefix}{Guid.NewGuid():N}{SiteTokens.JsonSuffix}");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, edited.ToJsonString());
            var token = TestContext.Current!.Execution.CancellationToken;
            var rejected = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.ReportOperation,
                Value: JsonSerializer.SerializeToElement(edited, SiteTokens.JsonOptions), ExpectedRevision: inputs.MeasuredRevision), token);
            await Assert.That(rejected.Property(SiteTokens.Ok).GetBoolean()).IsFalse();

            var rows = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.RowsOperation, temporaryPath,
                SiteTokens.PointRead, SiteTokens.ErrorMetric, SiteTokens.ZeroRepetition), token);
            var keyLoad = rows.Value.EnumerateArray().Single(row => row.GetProperty(SiteTokens.Name).GetString() == SiteTokens.KeyLoadTarget);
            await Assert.That(keyLoad.GetProperty(SiteTokens.Status).GetString()).IsEqualTo(SiteTokens.FailedStatus);
            await Assert.That(keyLoad.GetProperty(SiteTokens.Attempts).GetInt32()).IsEqualTo(operations);
            await Assert.That(keyLoad.GetProperty(SiteTokens.Value).GetDouble()).IsEqualTo(100d / operations).Within(SiteTokens.NumericTolerance);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task CompareRows(SiteTestInputs inputs, SiteReport report, string reportPath,
        string scenario, string metric, string repetition, CancellationToken cancellationToken)
    {
        var actual = await SiteNodeProbe.RunAsync(inputs,
            new(SiteTokens.RowsOperation, reportPath, scenario, metric, repetition), cancellationToken);
        var expected = SiteMeasurementOracle.Rows(report, scenario, metric, repetition);
        await Assert.That(actual.Value.GetArrayLength()).IsEqualTo(expected.Count);
        for (var index = SiteTokens.Zero; index < expected.Count; index++)
        {
            var row = actual.Value[index];
            var wanted = expected[index];
            await Assert.That(row.GetProperty(SiteTokens.Name).GetString()).IsEqualTo(wanted.Name);
            await Assert.That(row.GetProperty(SiteTokens.Status).GetString()).IsEqualTo(wanted.Status);
            await AssertNullableNumber(row, SiteTokens.Value, wanted.Value);
            await AssertNullableNumber(row, SiteTokens.Minimum, wanted.Minimum);
            await AssertNullableNumber(row, SiteTokens.Maximum, wanted.Maximum);
            await Assert.That(row.GetProperty(SiteTokens.Attempts).GetInt32()).IsEqualTo(wanted.Attempts);
            await Assert.That(row.GetProperty(SiteTokens.Successes).GetInt32()).IsEqualTo(wanted.Successes);
            await Assert.That(row.GetProperty(SiteTokens.Failures).GetInt32()).IsEqualTo(wanted.Failures);
            await AssertNullableNumber(row, SiteTokens.Throughput, wanted.Throughput);
            await AssertNullableNumber(row, SiteTokens.RowP50, wanted.P50);
            await AssertNullableNumber(row, SiteTokens.RowP95, wanted.P95);
            await AssertNullableNumber(row, SiteTokens.RowP99, wanted.P99);
            var detail = row.TryGetProperty(SiteTokens.Detail, out var detailElement) ? detailElement.GetString() : null;
            await Assert.That(detail).IsEqualTo(wanted.Detail);
        }
    }

    private static async Task AssertNullableNumber(JsonElement row, string property, double? expected)
    {
        var element = row.GetProperty(property);
        if (expected is null)
        {
            await Assert.That(element.ValueKind).IsEqualTo(JsonValueKind.Null);
            return;
        }

        await Assert.That(element.GetDouble()).IsEqualTo(expected.Value).Within(SiteTokens.NumericTolerance);
    }
}

internal static class SiteMeasurementOracle
{
    public static List<OracleRow> Rows(SiteReport report, string scenario, string metric, string repetition)
    {
        var rows = report.Targets.Select((target, index) => CreateRow(report, target, index, scenario, metric, repetition)).ToList();
        return metric == SiteTokens.ThroughputMetric
            ? rows.OrderBy(row => row.Value is null).ThenByDescending(row => row.Value).ThenBy(row => row.TargetIndex).ToList()
            : rows.OrderBy(row => row.Value is null).ThenBy(row => row.Value).ThenBy(row => row.TargetIndex).ToList();
    }

    private static OracleRow CreateRow(SiteReport report, SiteTarget target, int index,
        string scenario, string metric, string repetition)
    {
        var cases = report.Cases.Where(item => item.Target == target.Name && item.Scenario == scenario &&
            (repetition == SiteTokens.MedianRepetition ||
             item.Repetition == int.Parse(repetition, CultureInfo.InvariantCulture))).ToArray();
        var measured = cases.Where(item => item.Measurement is not null).Select(item => item.Measurement!).ToArray();
        var values = measured.Select(item => MetricValue(item, metric)).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        var attempts = measured.Sum(item => item.Attempts);
        var failures = measured.Sum(item => item.Failures);
        var status = cases.Any(item => item.Status == SiteTokens.FailedStatus)
            ? SiteTokens.FailedStatus : measured.Length > SiteTokens.Zero ? SiteTokens.MeasuredStatus : SiteTokens.UnsupportedStatus;
        var value = metric == SiteTokens.ErrorMetric && attempts > SiteTokens.Zero
            ? failures / (double)attempts * SiteTokens.ErrorPercentageScale : Median(values);
        return new(target.Name, status, value, values.Length == SiteTokens.Zero ? null : values.Min(),
            values.Length == SiteTokens.Zero ? null : values.Max(),
            attempts, measured.Sum(item => item.Successes), failures, Median(measured.Select(item => item.UsefulOperationsPerSecond)),
            Median(measured.Select(item => item.Latency.P50Ms)), Median(measured.Select(item => item.Latency.P95Ms)),
            Median(measured.Select(item => item.Latency.P99Ms)), cases.FirstOrDefault(item => item.Detail is not null)?.Detail, index);
    }

    private static double? MetricValue(SiteMeasurement measurement, string metric) => metric switch
    {
        SiteTokens.ThroughputMetric => measurement.UsefulOperationsPerSecond,
        SiteTokens.P50Metric => measurement.Latency.P50Ms,
        SiteTokens.P95Metric => measurement.Latency.P95Ms,
        SiteTokens.P99Metric => measurement.Latency.P99Ms,
        SiteTokens.ErrorMetric => measurement.Attempts == SiteTokens.Zero ? null :
            measurement.Failures / (double)measurement.Attempts * SiteTokens.ErrorPercentageScale,
        SiteTokens.EnqueueMetric => measurement.Enqueue?.P99Ms,
        SiteTokens.ReceiveMetric => measurement.Receive?.P99Ms,
        SiteTokens.AckMetric => measurement.Ack?.P99Ms,
        SiteTokens.CpuMetric => measurement.ClientResources is null ? null :
            measurement.ClientResources.CpuSeconds * SiteTokens.MillisecondsPerSecond / measurement.Attempts,
        SiteTokens.AllocationMetric => measurement.ClientResources is null ? null :
            measurement.ClientResources.AllocatedBytes / (double)SiteTokens.BytesPerKilobyte / measurement.Attempts,
        SiteTokens.RssMetric => measurement.ClientResources is null ? null :
            measurement.ClientResources.PeakObservedWorkingSetBytes / (double)SiteTokens.BytesPerMebibyte,
        _ => throw new InvalidOperationException(SiteTokens.OracleMetricError),
    };

    private static double? Median(IEnumerable<double> values)
    {
        var ordered = values.Where(double.IsFinite).Order().ToArray();
        if (ordered.Length == SiteTokens.Zero)
        {
            return null;
        }
        var middle = ordered.Length / SiteTokens.MedianDivisor;
        return ordered.Length % SiteTokens.MedianDivisor == SiteTokens.One
            ? ordered[middle]
            : (ordered[middle - SiteTokens.One] + ordered[middle]) / (double)SiteTokens.MedianDivisor;
    }
}

internal sealed record OracleRow(string Name, string Status, double? Value, double? Minimum, double? Maximum,
    int Attempts, int Successes, int Failures, double? Throughput, double? P50, double? P95, double? P99,
    string? Detail, int TargetIndex);
