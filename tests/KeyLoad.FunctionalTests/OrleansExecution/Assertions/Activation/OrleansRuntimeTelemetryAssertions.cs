using System.Diagnostics;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class OrleansRuntimeTelemetryAssertions
{
    internal static async Task AssertIsolatedParentAsync(Activity parent)
    {
        await Assert.That(parent.Parent).IsNull();
        await Assert.That(parent.TraceId).IsNotEqualTo(default(ActivityTraceId));
        await Assert.That(parent.ParentSpanId).IsNotEqualTo(default(ActivitySpanId));
        await Assert.That((parent.ActivityTraceFlags & ActivityTraceFlags.Recorded) != ActivityTraceFlags.None).IsTrue();
        await Assert.That(parent.Baggage.Any()).IsFalse();
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
    }

    internal static async Task AssertActivityRestoredAsync(Activity? expected)
        => await Assert.That(ReferenceEquals(Activity.Current, expected)).IsTrue();

    internal static async Task AssertMetricPrivacyAsync(OrleansMetricPointCapture[] metrics)
    {
        var execution = metrics.Where(static metric => metric.MeterName == OrleansRuntimeTelemetryTokens.MetricMeter).ToArray();
        await Assert.That(execution.Length).IsGreaterThan(0);
        foreach (var point in execution)
        {
            await Assert.That(point.Tags.Length).IsEqualTo(0);
            await Assert.That(point.ExemplarTags.Length).IsEqualTo(0);
        }

        await AssertSuppressionReasonAsync(metrics, OrleansRuntimeTelemetryTokens.SafeSuppressionReason);
        await AssertSuppressionReasonAsync(metrics, OrleansRuntimeTelemetryTokens.BaggageSuppressionReason);
        await AssertSuppressionReasonAsync(metrics, OrleansRuntimeTelemetryTokens.LinkSuppressionReason);
    }

    private static async Task AssertSuppressionReasonAsync(OrleansMetricPointCapture[] metrics, string reason)
    {
        var present = metrics.Any(point => point.MeterName == OrleansRuntimeTelemetryTokens.PrivacyMeter
            && point.MetricName == OrleansRuntimeTelemetryTokens.SuppressionCounter
            && point.Tags.Any(tag => tag.Key == OrleansRuntimeTelemetryTokens.SuppressionReasonTag
                && tag.Value == reason));
        await Assert.That(present).IsTrue();
    }
}
