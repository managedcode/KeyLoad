using System.Diagnostics;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum OrleansRuntimeTelemetryMutation
{
    Event,
    Baggage,
    Link,
    ErrorStatus
}

internal sealed class OrleansRuntimeTelemetrySentinel : IDisposable
{
    private readonly System.Threading.Lock gate = new();
    private readonly ActivityListener listener;
    private readonly OrleansRuntimeTelemetryMutation mutation;
    private const string RequestStreamMethodName = nameof(IRequestGrain.ExecuteStreamAsync);
    private const string CapabilityMethodName = nameof(IDatabaseReadGrain.ExecuteAsync);
    private const string MethodNameSeparator = "/";
    private const string NativeRequestStreamStartMethod =
        "Orleans.Runtime.IAsyncEnumerableGrainExtension/StartEnumeration<T>";
    private readonly int baggageLimit;
    private readonly ActivityTraceId callerTraceId;
    private readonly ActivitySpanId callerSpanId;
    private readonly OrleansActivityCaptureExporter exporter;
    private ActivityTraceId selectedTraceId;
    private ActivitySpanId selectedSpanId;
    private bool isApplicationSource;
    private bool isLifecycleSource;
    private bool isOtherSource;
    private bool matchedCallerParent;
    private ActivityStatusCode statusAtStart;
    private ActivityStatusCode statusAfterInjection;
    private ActivityStatusCode statusAtStop;
    private bool recordedAtStart;
    private bool recordedAfterInjection;
    private bool recordedAtStop;
    private int baggageCountAtStart;
    private int baggageCountAfterInjection;
    private int baggageCountAtStop;
    private bool isRequestMethod;
    private bool isCapabilityMethod;
    private bool isGenericMethod;
    private bool wasStopped;
    private bool exportMatched;
    private int injected;

    internal OrleansRuntimeTelemetrySentinel(OrleansRuntimeTelemetryMutation mutation, int baggageLimit,
        Activity caller, OrleansActivityCaptureExporter exporter)
    {
        this.mutation = mutation;
        this.baggageLimit = baggageLimit;
        callerTraceId = caller.TraceId;
        callerSpanId = caller.SpanId;
        this.exporter = exporter;
        listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == OrleansRuntimeTelemetryTokens.ApplicationSource,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = Inject,
            ActivityStopped = ObserveStop
        };
        ActivitySource.AddActivityListener(listener);
    }

    internal bool WasInjected => Volatile.Read(ref injected) == 1;

    internal OrleansRuntimeTelemetrySentinelObservation Snapshot()
    {
        lock (gate)
        {
            return new(mutation, WasInjected, isApplicationSource, isLifecycleSource, isOtherSource,
                isRequestMethod, isCapabilityMethod, isGenericMethod, matchedCallerParent, statusAtStart,
                statusAfterInjection, statusAtStop, recordedAtStart, recordedAfterInjection, recordedAtStop,
                baggageCountAtStart, baggageCountAfterInjection, baggageCountAtStop, wasStopped, exportMatched);
        }
    }

    public void Dispose() => listener.Dispose();

    private void Inject(Activity activity)
    {
        lock (gate)
        {
            ObserveInjection(activity);
        }
    }

    private void ObserveInjection(Activity activity)
    {
        if (Interlocked.CompareExchange(ref injected, 1, 0) != 0)
        {
            return;
        }

        selectedTraceId = activity.TraceId;
        selectedSpanId = activity.SpanId;
        isApplicationSource = activity.Source.Name == OrleansRuntimeTelemetryTokens.ApplicationSource;
        isLifecycleSource = activity.Source.Name == OrleansRuntimeTelemetryTokens.LifecycleSource;
        isOtherSource = !isApplicationSource && !isLifecycleSource;
        matchedCallerParent = activity.TraceId == callerTraceId && activity.ParentSpanId == callerSpanId;
        statusAtStart = activity.Status;
        recordedAtStart = IsRecorded(activity);
        baggageCountAtStart = CountBaggage(activity);
        InjectMutation(activity);
        statusAfterInjection = activity.Status;
        recordedAfterInjection = IsRecorded(activity);
        baggageCountAfterInjection = CountBaggage(activity);
    }

    private void InjectMutation(Activity activity)
    {
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
                    activity.SetBaggage(OrleansRuntimeTelemetryTokens.LateBaggagePrefix
                        + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        OrleansRuntimeTelemetryTokens.SentinelValue);
                }
                break;
            case OrleansRuntimeTelemetryMutation.Link:
                var context = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
                    ActivityTraceFlags.Recorded);
                activity.AddLink(new(context));
                break;
            case OrleansRuntimeTelemetryMutation.ErrorStatus:
                activity.SetStatus(ActivityStatusCode.Error, OrleansRuntimeTelemetryTokens.SentinelValue);
                break;
        }
    }

    private void ObserveStop(Activity activity)
    {
        lock (gate)
        {
            ObserveSelectedStop(activity);
        }
    }

    private void ObserveSelectedStop(Activity activity)
    {
        if (activity.TraceId != selectedTraceId || activity.SpanId != selectedSpanId)
        {
            return;
        }

        wasStopped = true;
        statusAtStop = activity.Status;
        recordedAtStop = IsRecorded(activity);
        baggageCountAtStop = CountBaggage(activity);
        ObserveMethod(activity);
        exportMatched = exporter.Snapshot().Any(capture => capture.TraceId == selectedTraceId.ToHexString()
            && capture.SpanId == selectedSpanId.ToHexString());
    }

    private void ObserveMethod(Activity activity)
    {
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key == OrleansRuntimeTelemetryTokens.RpcMethodTag && tag.Value is string method)
            {
                isRequestMethod = IsRequestMethod(method);
                isCapabilityMethod = IsCapabilityMethod(method);
                isGenericMethod = !isRequestMethod && !isCapabilityMethod;
                return;
            }
        }
    }

    private static bool IsRequestMethod(string method)
        => method is OrleansRuntimeTelemetryTokens.RequestMethodValue or RequestStreamMethodName
            or NativeRequestStreamStartMethod
            || method.EndsWith(MethodNameSeparator + RequestStreamMethodName, StringComparison.Ordinal);

    private static bool IsCapabilityMethod(string method)
        => method is OrleansRuntimeTelemetryTokens.CapabilityMethodValue or CapabilityMethodName
            || method.EndsWith(MethodNameSeparator + CapabilityMethodName, StringComparison.Ordinal);

    private int CountBaggage(Activity activity)
    {
        var maximumObserved = baggageLimit + 1;
        var count = 0;
        using var baggage = activity.Baggage.GetEnumerator();
        while (count < maximumObserved && baggage.MoveNext())
        {
            count++;
        }

        return count;
    }

    private static bool IsRecorded(Activity activity)
        => (activity.ActivityTraceFlags & ActivityTraceFlags.Recorded) != ActivityTraceFlags.None;
}
