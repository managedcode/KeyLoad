using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum OrleansRuntimeTelemetryMutation
{
    Event,
    Baggage,
    Link
}

internal sealed class OrleansRuntimeTelemetrySentinel : IDisposable
{
    private readonly ActivityListener listener;
    private readonly OrleansRuntimeTelemetryMutation mutation;
    private readonly int baggageLimit;
    private int injected;

    internal OrleansRuntimeTelemetrySentinel(OrleansRuntimeTelemetryMutation mutation, int baggageLimit = 0)
    {
        this.mutation = mutation;
        this.baggageLimit = baggageLimit;
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

        switch (mutation)
        {
            case OrleansRuntimeTelemetryMutation.Event:
                var attributes = new ActivityTagsCollection
                {
                    [OrleansRuntimeTelemetryTokens.SentinelTag] = OrleansRuntimeTelemetryTokens.SentinelValue
                };
                activity.AddEvent(new(OrleansRuntimeTelemetryTokens.SentinelEvent, tags: attributes));
                break;
            case OrleansRuntimeTelemetryMutation.Baggage:
                for (var index = 0; index <= baggageLimit; index++)
                {
                    activity.SetBaggageItem(OrleansRuntimeTelemetryTokens.LateBaggagePrefix
                        + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        OrleansRuntimeTelemetryTokens.SentinelValue);
                }
                break;
            case OrleansRuntimeTelemetryMutation.Link:
                var context = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
                    ActivityTraceFlags.Recorded);
                activity.AddLink(new(context));
                break;
        }
    }
}
