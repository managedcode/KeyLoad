using System.Diagnostics;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class OrleansTelemetryAncestryObservation : IDisposable
{
    private readonly Lock gate = new();
    private readonly List<(string SpanId, bool Recorded)> spans = [];
    private readonly ActivityListener listener;
    private bool truncated;

    internal OrleansTelemetryAncestryObservation(Activity parent)
    {
        var traceId = parent.TraceId;
        var parentId = parent.SpanId;
        listener = new()
        {
            ShouldListenTo = static source => source.Name == OrleansRuntimeTelemetryTokens.ApplicationSource,
            ActivityStopped = activity =>
            {
                if (activity.Kind != ActivityKind.Client || activity.TraceId != traceId
                    || activity.ParentSpanId != parentId)
                {
                    return;
                }
                lock (gate)
                {
                    if (spans.Count == OrleansTelemetryAncestryProtocol.MaximumSelectedSpans)
                    {
                        truncated = true;
                        return;
                    }
                    spans.Add((activity.SpanId.ToHexString(), activity.Recorded));
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
    }

    internal async Task RequireAsync(OrleansRuntimeTelemetryFixture telemetry, bool expectedRecorded)
    {
        (string SpanId, bool Recorded)[] observed;
        bool lost;
        lock (gate)
        {
            observed = [.. spans];
            lost = truncated;
        }
        await Assert.That(lost).IsFalse();
        await Assert.That(observed.Length).IsGreaterThan(0);
        telemetry.Flush();
        var exported = telemetry.Activities.Snapshot();
        foreach (var span in observed)
        {
            await Assert.That(span.Recorded).IsEqualTo(expectedRecorded);
            await Assert.That(exported.Any(row => row.SourceName == OrleansRuntimeTelemetryTokens.ApplicationSource
                && row.SpanId == span.SpanId)).IsEqualTo(expectedRecorded);
        }
        await Assert.That(telemetry.Activities.WasTruncated).IsFalse();
        await Assert.That(telemetry.Metrics.WasTruncated).IsFalse();
    }

    public void Dispose() => listener.Dispose();
}
