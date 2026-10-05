using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Checks the complete caller-visible native vector result without deriving unavailable metrics.</summary>
internal static class IsolatedNativeVectorAssertions
{
    internal static async Task VerifyAsync(ComparisonReport report, ComparisonWorkerSelection selection,
        VectorComparisonProfile profile)
    {
        await Assert.That(report.Options).IsNull();
        await Assert.That(report.ScaledProfile).IsNull();
        await Assert.That(report.VectorProfile).IsEqualTo(profile);
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(report.DatasetSha256,
            "^[a-f0-9]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)).IsTrue();
        var target = report.Targets.Single();
        await Assert.That(target.Name).IsEqualTo(selection.Target);
        await Assert.That(target.Cluster!.Nodes).IsEqualTo(selection.NodeCount);
        await Assert.That(target.Cluster.DataCopies).IsEqualTo(selection.NodeCount);
        await Assert.That(target.Cluster.Observations.IsDefaultOrEmpty).IsFalse();
        var item = report.Cases.Single();
        await Assert.That(item.Repetition).IsEqualTo(0);
        await Assert.That(item.Target).IsEqualTo(selection.Target);
        await Assert.That(item.Scenario).IsEqualTo(Scenario.VectorExact);
        await Assert.That(item.Status).IsEqualTo(ComparisonStatuses.Measured);
        await Assert.That(item.Measurement).IsNull();
        await Assert.That(item.Samples).IsEmpty();
        await VerifyCountersAndRecallAsync(item.VectorMetrics!, profile);
        await VerifyNativeMetricsAsync(item.VectorMetrics!, profile);
    }

    private static async Task VerifyCountersAndRecallAsync(VectorMetrics metrics, VectorComparisonProfile profile)
    {
        await Assert.That(metrics.RecordCount).IsEqualTo(profile.RecordCount);
        await Assert.That(metrics.LoadedRecordCount).IsEqualTo(profile.RecordCount);
        await Assert.That(metrics.QueryAttempts).IsEqualTo(profile.MeasuredQueries);
        await Assert.That(metrics.QuerySuccesses).IsEqualTo(profile.MeasuredQueries);
        await Assert.That(metrics.UpdateAttempts).IsEqualTo(profile.UpdateCount);
        await Assert.That(metrics.UpdateSuccesses).IsEqualTo(profile.UpdateCount);
        await Assert.That(metrics.RecallSamples).IsEqualTo(profile.MeasuredQueries);
        await Assert.That(metrics.PerQueryRecall.Length).IsEqualTo(profile.MeasuredQueries);
        await Assert.That(metrics.PerQueryRecall.All(value => double.IsFinite(value) && value >= 0 && value <= 1)).IsTrue();
        await Assert.That(metrics.ExactRecall).IsEqualTo(metrics.PerQueryRecall.Average());
        await Assert.That(metrics.MinimumRecall).IsEqualTo(metrics.PerQueryRecall.Min());
        await Assert.That(metrics.ExactRecall).IsGreaterThanOrEqualTo(profile.MinimumRecall);
    }

    private static async Task VerifyNativeMetricsAsync(VectorMetrics metrics, VectorComparisonProfile profile)
    {
        await Assert.That(double.IsFinite(metrics.LatencyP95Ms) && metrics.LatencyP95Ms > 0).IsTrue();
        await Assert.That(metrics.LatencyP99Ms).IsGreaterThanOrEqualTo(metrics.LatencyP95Ms);
        await Assert.That(metrics.IndexKind).IsEqualTo(profile.IndexKind.ToString());
        await Assert.That(double.IsFinite(metrics.IndexBuildMilliseconds) && metrics.IndexBuildMilliseconds >= 0).IsTrue();
        await Assert.That(string.IsNullOrWhiteSpace(metrics.NativeIndexDefinition)).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(metrics.NativeQueryPlan)).IsFalse();
        await Assert.That(metrics.ServerMemoryBytes).IsNull();
        await Assert.That(metrics.ServerMemorySamplingIntervalMs).IsNull();
        await Assert.That(double.IsFinite(metrics.QueryElapsedSeconds) && metrics.QueryElapsedSeconds > 0).IsTrue();
        await Assert.That(metrics.QueryUsefulOperationsPerSecond).IsEqualTo(profile.MeasuredQueries / metrics.QueryElapsedSeconds);
        if (profile.IndexKind == VectorIndexKind.Exact)
        {
            await Assert.That(metrics.PerQueryRecall.All(value => value == 1)).IsTrue();
            await Assert.That(metrics.IndexBuildMilliseconds).IsEqualTo(0);
        }
        else
        {
            await Assert.That(metrics.IndexBuildMilliseconds).IsGreaterThan(0);
        }
        if (profile.UpdateCount == 0)
        {
            await Assert.That(metrics.UpdateElapsedSeconds).IsEqualTo(0);
            await Assert.That(metrics.UpdateUsefulOperationsPerSecond).IsEqualTo(0);
        }
        else
        {
            await Assert.That(double.IsFinite(metrics.UpdateElapsedSeconds) && metrics.UpdateElapsedSeconds > 0).IsTrue();
            await Assert.That(metrics.UpdateUsefulOperationsPerSecond).IsEqualTo(profile.UpdateCount / metrics.UpdateElapsedSeconds);
        }
    }
}
