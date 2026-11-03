using System.Diagnostics;
using System.Diagnostics.Metrics;
using KeyLoad.Server.OrleansHosting.Configuration;
using KeyLoad.Server.OrleansHosting.Contracts;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KeyLoad.Server.OrleansHosting.Diagnostics;

/// <summary>Scrubs execution Orleans activities before they reach any configured exporter.</summary>
internal sealed class OrleansTelemetryPrivacyProcessor(IOptions<OrleansTelemetryOptions> configuredOptions)
    : BaseProcessor<Activity>
{
    private static readonly Meter PrivacyMeter = new(OrleansTelemetryPolicy.PrivacyMeterName);
    private static readonly Counter<long> SuppressedSpans = PrivacyMeter.CreateCounter<long>(
        OrleansTelemetryPolicy.SuppressedSpanCounterName);
    private const int InitialItemCount = 0;
    private const long SuppressedSpanIncrement = 1;
    private OrleansTelemetryOptions Options => configuredOptions.Value;

    public override void OnStart(Activity data)
    {
        if (!IsApprovedSource(data.Source.Name))
        {
            return;
        }

        data.TraceStateString = null;
        if (!OrleansTelemetryAncestry.IsWithinLimit(data, Options.MaximumParentLinks) || ClearBoundedBaggage(data))
        {
            Suppress(data, OrleansTelemetryPolicy.SuppressionBaggageLimit);
        }
    }

    public override void OnEnd(Activity data)
    {
        if (!IsApprovedSource(data.Source.Name))
        {
            return;
        }

        if ((data.ActivityTraceFlags & ActivityTraceFlags.Recorded) == ActivityTraceFlags.None)
        {
            return;
        }

        data.TraceStateString = null;
        if (!OrleansTelemetryAncestry.IsWithinLimit(data, Options.MaximumParentLinks) || ClearBoundedBaggage(data))
        {
            Suppress(data, OrleansTelemetryPolicy.SuppressionBaggageLimit);
            return;
        }

        if (SuppressExcessTags(data) || SuppressUnsafeEvents(data) || SuppressLinkedActivity(data))
        {
            return;
        }

        OrleansTelemetryTagSanitizer.Scrub(data, Options.MaximumTagValueCharacters);
        data.DisplayName = data.Source.Name == OrleansTelemetryPolicy.ApplicationActivitySourceName
            ? OrleansTelemetryPolicy.ApplicationDisplayName
            : OrleansTelemetryPolicy.LifecycleDisplayName;
        if (!string.IsNullOrEmpty(data.StatusDescription))
        {
            data.SetStatus(data.Status, null);
        }
    }

    internal static (bool HasValue, MetricStreamConfiguration? Value) ConfigureMetricView(Instrument instrument)
        => string.Equals(instrument.Meter.Name, OrleansTelemetryPolicy.OrleansMeterName, StringComparison.Ordinal)
            ? (true, (MetricStreamConfiguration)(new MetricStreamConfiguration { TagKeys = [] }))
            : (false, default(MetricStreamConfiguration?));

    private static bool IsApprovedSource(string sourceName)
        => sourceName is OrleansTelemetryPolicy.ApplicationActivitySourceName
            or OrleansTelemetryPolicy.LifecycleActivitySourceName;

    private static bool IsSafeLifecycleEvent(ActivityEvent activityEvent)
        => HasNoEventTags(activityEvent) && activityEvent.Name is
            OrleansTelemetryPolicy.EventInstanceCreated or OrleansTelemetryPolicy.EventRehydrated
            or OrleansTelemetryPolicy.EventActivationStart or OrleansTelemetryPolicy.EventDirectoryRetryRecovery
            or OrleansTelemetryPolicy.EventRetryRecovery or OrleansTelemetryPolicy.EventDirectoryRegisterSuccess
            or OrleansTelemetryPolicy.EventSuccess or OrleansTelemetryPolicy.EventDirectoryRetryPrevious
            or OrleansTelemetryPolicy.EventRetryPrevious or OrleansTelemetryPolicy.EventDuplicateActivation
            or OrleansTelemetryPolicy.EventDuplicate or OrleansTelemetryPolicy.EventDirectoryRegisterFailed
            or OrleansTelemetryPolicy.EventStateActivating or OrleansTelemetryPolicy.EventLifecycleStart
            or OrleansTelemetryPolicy.EventLifecycleStarted or OrleansTelemetryPolicy.EventLifecycleStartFailed
            or OrleansTelemetryPolicy.EventStateValid;

    private bool ClearBoundedBaggage(Activity activity)
    {
        var limit = Options.MaximumBaggageItems;
        List<string> keys;
        using (var baggage = activity.Baggage.GetEnumerator())
        {
            if (!baggage.MoveNext())
            {
                return false;
            }

            if (limit == 0)
            {
                return true;
            }

            keys = new List<string>(limit);
            do
            {
                keys.Add(baggage.Current.Key);
            }
            while (keys.Count < limit && baggage.MoveNext());
        }

        foreach (var key in keys)
        {
            activity.SetBaggage(key, null);
        }

        using var remainingBaggage = activity.Baggage.GetEnumerator();
        return remainingBaggage.MoveNext();
    }

    private static bool HasNoEventTags(ActivityEvent activityEvent)
    {
        using var /* KLD0041: The native ActivityEvent tag enumerator is object-valued; inspect only whether any tag exists and dispose it immediately. */ tags = ((activityEvent.Tags /* KLD0041: Inspect only whether the native ActivityEvent object-valued tags are empty before admitting the safe event shape. */).GetEnumerator() /* KLD0041: Inspect only whether the native ActivityEvent object-valued tags are empty before admitting the safe event shape. */);
        return !(tags.MoveNext() /* KLD0041: Probe the native ActivityEvent tag enumerator once; reject tagged events without reading their runtime values. */);
    }

    private static void Suppress(Activity activity, string reason)
    {
        activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        SuppressedSpans.Add(SuppressedSpanIncrement, (new KeyValuePair<string, object?>(
            OrleansTelemetryPolicy.SuppressionReasonTag, reason) /* KLD0041: The native metrics pair holds only the closed telemetry suppression-reason string, with no scalar boxing. */)) /* KLD0041: Emit the native suppression counter with one bounded closed reason tag; arbitrary Activity tag values are never forwarded. */;
    }

    private bool SuppressExcessTags(Activity activity)
    {
        var count = InitialItemCount;
        foreach (var _ in (activity.TagObjects /* KLD0041: Bound the native Activity object-valued tag count before allowing any span export. */))
        {
            if (++count <= Options.MaximumTags)
            {
                continue;
            }

            Suppress(activity, OrleansTelemetryPolicy.SuppressionTagLimit);
            return true;
        }

        return false;
    }

    private bool SuppressUnsafeEvents(Activity activity)
    {
        var count = InitialItemCount;
        foreach (var activityEvent in activity.Events)
        {
            if (++count > Options.MaximumEvents)
            {
                Suppress(activity, OrleansTelemetryPolicy.SuppressionEventLimit);
                return true;
            }

            if (activity.Source.Name == OrleansTelemetryPolicy.LifecycleActivitySourceName
                && IsSafeLifecycleEvent(activityEvent))
            {
                continue;
            }

            Suppress(activity, OrleansTelemetryPolicy.SuppressionUnsafeEvent);
            return true;
        }

        return false;
    }

    private static bool SuppressLinkedActivity(Activity activity)
    {
        if (!activity.Links.Any())
        {
            return false;
        }

        Suppress(activity, OrleansTelemetryPolicy.SuppressionLinksPresent);
        return true;
    }
}
