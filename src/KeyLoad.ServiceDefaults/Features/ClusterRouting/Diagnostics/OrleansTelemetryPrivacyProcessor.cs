using System.Diagnostics;
using System.Diagnostics.Metrics;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Configuration;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Contracts;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KeyLoad.ServiceDefaults.Features.ClusterRouting.Diagnostics;

/// <summary>Scrubs native Orleans activities before they reach any configured exporter.</summary>
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
        if (ClearBoundedBaggage(data))
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
        if (ClearBoundedBaggage(data))
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

    internal static MetricStreamConfiguration ConfigureMetricView(Instrument instrument)
        => string.Equals(instrument.Meter.Name, OrleansTelemetryPolicy.OrleansMeterName, StringComparison.Ordinal)
            ? new MetricStreamConfiguration { TagKeys = [] }
            : null!;

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
        var keys = new List<string>(Options.MaximumBaggageItems);
        foreach (var (key, _) in activity.Baggage)
        {
            if (keys.Count == Options.MaximumBaggageItems)
            {
                break;
            }

            keys.Add(key);
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
        using var tags = activityEvent.Tags.GetEnumerator();
        return !tags.MoveNext();
    }

    private static void Suppress(Activity activity, string reason)
    {
        activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        SuppressedSpans.Add(SuppressedSpanIncrement, new KeyValuePair<string, object?>(
            OrleansTelemetryPolicy.SuppressionReasonTag, reason));
    }

    private bool SuppressExcessTags(Activity activity)
    {
        var count = InitialItemCount;
        foreach (var _ in activity.TagObjects)
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
