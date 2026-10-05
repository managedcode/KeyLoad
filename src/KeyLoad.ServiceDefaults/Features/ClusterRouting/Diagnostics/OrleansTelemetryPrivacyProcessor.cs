using System.Diagnostics;
using System.Diagnostics.Metrics;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Contracts;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Configuration;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KeyLoad.ServiceDefaults.Features.ClusterRouting.Diagnostics;

/// <summary>Scrubs native Orleans activities before they reach any configured exporter.</summary>
internal sealed class OrleansTelemetryPrivacyProcessor : BaseProcessor<Activity>
{
    private static readonly Meter PrivacyMeter = new(OrleansTelemetryPolicy.PrivacyMeterName);
    private static readonly Counter<long> SuppressedSpans = PrivacyMeter.CreateCounter<long>(
        OrleansTelemetryPolicy.SuppressedSpanCounterName);
    private readonly OrleansTelemetryOptions options;

    public OrleansTelemetryPrivacyProcessor(IOptions<OrleansTelemetryOptions> configuredOptions)
        => options = configuredOptions.Value;

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

        if ((data.ActivityTraceFlags & ActivityTraceFlags.Recorded) == 0)
        {
            return;
        }

        data.TraceStateString = null;
        if (ClearBoundedBaggage(data))
        {
            Suppress(data, OrleansTelemetryPolicy.SuppressionBaggageLimit);
            return;
        }

        var tagCount = 0;
        foreach (var _ in data.TagObjects)
        {
            if (++tagCount > options.MaximumTags)
            {
                Suppress(data, OrleansTelemetryPolicy.SuppressionTagLimit);
                return;
            }
        }

        var eventCount = 0;
        foreach (var activityEvent in data.Events)
        {
            if (++eventCount > options.MaximumEvents)
            {
                Suppress(data, OrleansTelemetryPolicy.SuppressionEventLimit);
                return;
            }

            if (data.Source.Name != OrleansTelemetryPolicy.LifecycleActivitySourceName
                || !IsSafeLifecycleEvent(activityEvent))
            {
                Suppress(data, OrleansTelemetryPolicy.SuppressionUnsafeEvent);
                return;
            }
        }

        if (data.Links.Any())
        {
            Suppress(data, OrleansTelemetryPolicy.SuppressionLinksPresent);
            return;
        }

        ScrubTags(data);
        data.DisplayName = data.Source.Name == OrleansTelemetryPolicy.ApplicationActivitySourceName
            ? OrleansTelemetryPolicy.ApplicationDisplayName
            : OrleansTelemetryPolicy.LifecycleDisplayName;
        if (!string.IsNullOrEmpty(data.StatusDescription))
        {
            data.SetStatus(data.Status, null);
        }
    }

    internal static MetricStreamConfiguration? ConfigureMetricView(Instrument instrument)
        => string.Equals(instrument.Meter.Name, OrleansTelemetryPolicy.OrleansMeterName, StringComparison.Ordinal)
            ? new MetricStreamConfiguration { TagKeys = [] }
            : null;

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
        var keys = new List<string>(options.MaximumBaggageItems);
        foreach (var (key, _) in activity.Baggage)
        {
            if (keys.Count == options.MaximumBaggageItems)
            {
                foreach (var existingKey in keys)
                {
                    activity.SetBaggageItem(existingKey, null);
                }

                return true;
            }

            keys.Add(key);
        }

        foreach (var key in keys)
        {
            activity.SetBaggageItem(key, null);
        }

        return false;
    }

    private static bool HasNoEventTags(ActivityEvent activityEvent)
    {
        using var tags = activityEvent.Tags.GetEnumerator();
        return !tags.MoveNext();
    }

    private static void ScrubTags(Activity activity)
    {
        foreach (var (key, value) in activity.TagObjects.ToArray())
        {
            var normalized = NormalizeTag(key, value);
            if (normalized is null)
            {
                activity.SetTag(key, null);
            }
            else if (!Equals(normalized, value))
            {
                activity.SetTag(key, normalized);
            }
        }
    }

    private static object? NormalizeTag(string key, object? value)
    {
        if (key == OrleansTelemetryPolicy.ExceptionEscapedTag && value is bool escaped)
        {
            return escaped;
        }

        if (value is not string text || text.Length is 0 or > 256)
        {
            return null;
        }

        return key switch
        {
            OrleansTelemetryPolicy.RpcSystemTag when string.Equals(text,
                OrleansTelemetryPolicy.RpcSystemValue, StringComparison.Ordinal) => OrleansTelemetryPolicy.RpcSystemValue,
            OrleansTelemetryPolicy.RpcServiceTag => OrleansTelemetryPolicy.RpcServiceValue,
            OrleansTelemetryPolicy.RpcMethodTag => NormalizeMethod(text),
            OrleansTelemetryPolicy.GrainTypeTag => OrleansTelemetryPolicy.GrainTypeValue,
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseNewValue, StringComparison.Ordinal)
                => OrleansTelemetryPolicy.ActivationCauseNewValue,
            OrleansTelemetryPolicy.ActivationCauseTag when string.Equals(text,
                OrleansTelemetryPolicy.ActivationCauseRehydrateValue, StringComparison.Ordinal)
                => OrleansTelemetryPolicy.ActivationCauseRehydrateValue,
            OrleansTelemetryPolicy.ExceptionTypeTag => OrleansTelemetryPolicy.ExceptionTypeValue,
            _ => null
        };
    }

    private static string NormalizeMethod(string method)
        => method switch
        {
            OrleansTelemetryPolicy.RequestStreamMethodName => OrleansTelemetryPolicy.RequestMethodValue,
            OrleansTelemetryPolicy.CapabilityMethodName => OrleansTelemetryPolicy.CapabilityMethodValue,
            _ when method.EndsWith(OrleansTelemetryPolicy.MethodNameSeparator
                + OrleansTelemetryPolicy.RequestStreamMethodName, StringComparison.Ordinal)
                => OrleansTelemetryPolicy.RequestMethodValue,
            _ when method.EndsWith(OrleansTelemetryPolicy.MethodNameSeparator
                + OrleansTelemetryPolicy.CapabilityMethodName, StringComparison.Ordinal)
                => OrleansTelemetryPolicy.CapabilityMethodValue,
            _ => OrleansTelemetryPolicy.GenericMethodValue
        };

    private static void Suppress(Activity activity, string reason)
    {
        activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        SuppressedSpans.Add(1, new KeyValuePair<string, object?>(
            OrleansTelemetryPolicy.SuppressionReasonTag, reason));
    }
}
