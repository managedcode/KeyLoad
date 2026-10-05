using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class OrleansRuntimeTelemetrySentinel : IDisposable
{
    private readonly ActivityListener listener;
    private int injected;

    internal OrleansRuntimeTelemetrySentinel()
    {
        listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == OrleansRuntimeTelemetryTokens.ApplicationSource,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = Inject
        };
        ActivitySource.AddActivityListener(listener);
    }

    internal bool WasInjected => Volatile.Read(ref injected) == 1;

    public void Dispose() => listener.Dispose();

    private void Inject(Activity activity)
    {
        if (Interlocked.CompareExchange(ref injected, 1, 0) != 0)
        {
            return;
        }

        var attributes = new ActivityTagsCollection
        {
            [OrleansRuntimeTelemetryTokens.SentinelTag] = OrleansRuntimeTelemetryTokens.SentinelValue
        };
        activity.AddEvent(new(OrleansRuntimeTelemetryTokens.SentinelEvent, tags: attributes));
    }
}
