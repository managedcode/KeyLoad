using System.Diagnostics;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class OrleansTelemetryCaptureIsolationTests
{
    private const int CaptureRecords = 2;
    private const int BackgroundActivities = 4;
    private const string Operation = "capture-isolation";

    [Test]
    public async Task UnrelatedTracesLeaveCapacityForTheCompleteOwnedTrace()
    {
        await using var telemetry = await OrleansRuntimeTelemetryFixture.StartAsync(new() { MaximumRecords = CaptureRecords });
        using var source = new ActivitySource(OrleansRuntimeTelemetryTokens.ApplicationSource);
        EmitActivities(source, BackgroundActivities);
        ActivityTraceId traceId;
        using (var parent = telemetry.StartParentActivity() ?? throw new InvalidOperationException())
        {
            traceId = parent.TraceId;
            using var child = source.StartActivity(Operation) ?? throw new InvalidOperationException();
        }
        telemetry.Flush();
        var captures = telemetry.Activities.Snapshot();
        await Assert.That(captures.Length).IsEqualTo(CaptureRecords);
        await Assert.That(captures.All(capture => capture.TraceId == traceId.ToHexString())).IsTrue();
        await Assert.That(telemetry.Activities.WasTruncated).IsFalse();
    }

    [Test]
    public async Task AnOwnedTraceStillReportsExhaustedCaptureCapacity()
    {
        await using var telemetry = await OrleansRuntimeTelemetryFixture.StartAsync(new() { MaximumRecords = CaptureRecords });
        using var source = new ActivitySource(OrleansRuntimeTelemetryTokens.ApplicationSource);
        using (telemetry.StartParentActivity() ?? throw new InvalidOperationException())
        {
            EmitActivities(source, BackgroundActivities);
        }
        telemetry.Flush();
        await Assert.That(telemetry.Activities.Snapshot().Length).IsEqualTo(CaptureRecords);
        await Assert.That(telemetry.Activities.WasTruncated).IsTrue();
    }

    private static void EmitActivities(ActivitySource source, int count)
    {
        for (var index = 0; index < count; index++)
        {
            using var activity = source.StartActivity(Operation) ?? throw new InvalidOperationException();
        }
    }
}
