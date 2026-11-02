using System.Diagnostics;
using System.Diagnostics.Metrics;
using KeyLoad.Server.Authorization.Configuration;
using KeyLoad.Server.Authorization.Contracts;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KeyLoad.Server.Authorization.Diagnostics;

internal sealed class HttpTelemetryPrivacyProcessor(IOptions<HttpTelemetryPrivacyOptions> options) : BaseProcessor<Activity>
{
    private static readonly Meter PrivacyMeter = new(HttpTelemetryPrivacyPolicy.PrivacyMeter);
    private static readonly Counter<long> Suppressed = PrivacyMeter.CreateCounter<long>(HttpTelemetryPrivacyPolicy.SuppressedCounter);
    public override void OnStart(Activity data)
    {
        if (HttpTelemetryPrivacyPolicy.IsHttp(data.Source.Name))
        {
            data.TraceStateString = null;
        }
    }
    public override void OnEnd(Activity data)
    {
        if (!HttpTelemetryPrivacyPolicy.IsHttp(data.Source.Name) || !data.Recorded)
        {
            return;
        }
        data.TraceStateString = null;
        var linkReason = (HttpTelemetryPrivacyPolicy.LinkSuppressionReason(data)).Value;
        if (linkReason is not null)
        {
            Suppress(data, linkReason);
            return;
        }
        if (data.Events.Any())
        {
            Suppress(data, HttpTelemetryPrivacyPolicy.UnsafeStateReason);
            return;
        }
        if (!ClearBaggage(data) || !ScrubTags(data))
        {
            Suppress(data, HttpTelemetryPrivacyPolicy.ExcessStateReason);
            return;
        }
        data.DisplayName = data.Source.Name == HttpTelemetryPrivacyPolicy.ServerSource
            ? HttpTelemetryPrivacyPolicy.ServerDisplay : HttpTelemetryPrivacyPolicy.ClientDisplay;
        data.SetStatus(data.Status, null);
    }
    internal static (bool HasValue, MetricStreamConfiguration? Value) ConfigureMetricView(Instrument instrument)
        => instrument.Meter.Name is HttpTelemetryPrivacyPolicy.ServerMeter or HttpTelemetryPrivacyPolicy.ClientMeter
            ? (true, (MetricStreamConfiguration)(new MetricStreamConfiguration { TagKeys = [] })) : (false, default(MetricStreamConfiguration?));
    private bool ClearBaggage(Activity data)
    {
        if (!HasBoundedAncestry(data))
        {
            return false;
        }
        var keys = new List<string>(options.Value.MaximumBaggageItems);
        foreach (var item in data.Baggage)
        {
            if (keys.Count == options.Value.MaximumBaggageItems)
            {
                return false;
            }
            keys.Add(item.Key);
        }
        foreach (var key in keys)
        {
            data.SetBaggage(key, null);
        }
        using var remaining = data.Baggage.GetEnumerator();
        return !remaining.MoveNext();
    }
    private bool HasBoundedAncestry(Activity data)
    {
        var examined = 0;
        for (var parent = data.Parent; parent is not null; parent = parent.Parent)
        {
            if (++examined > options.Value.MaximumParentLinks)
            {
                return false;
            }
        }
        return true;
    }
    private bool ScrubTags(Activity data)
    {
        var keys = new List<string>(options.Value.MaximumTags);
        foreach (var item in (data.TagObjects /* KLD0041: Inspect the native Activity object-valued tags under MaximumTags before replacing them with the bounded HTTP privacy allowlist. */))
        {
            if (keys.Count == options.Value.MaximumTags)
            {
                return false;
            }
            keys.Add(item.Key);
        }
        var method = (HttpTelemetryPrivacyPolicy.NormalizeMethod((data.GetTagItem(HttpTelemetryPrivacyPolicy.MethodTag) /* KLD0041: Read the native Activity method tag for closed HTTP privacy validation. */)) /* KLD0041: Map the native Activity method tag into the fixed public HTTP method allowlist before republishing it. */);
        var /* KLD0041: Retain the original Activity HTTP status tag box after integer-range validation so publishing the sanitized status does not box it again. */ status = (data.GetTagItem(HttpTelemetryPrivacyPolicy.StatusTag) /* KLD0041: Read the native Activity status tag for closed HTTP privacy validation. */);
        var protocol = ((HttpTelemetryPrivacyPolicy.NormalizeProtocol((data.GetTagItem(HttpTelemetryPrivacyPolicy.ProtocolTag) /* KLD0041: Read the native Activity protocol tag for closed HTTP privacy validation. */)) /* KLD0041: Map the native Activity protocol tag into the fixed supported protocol allowlist before republishing it. */)).Value;
        foreach (var key in keys)
        {
            data.SetTag(key, null) /* KLD0041: Remove this original Activity tag through the native object-valued API before publishing only approved HTTP metadata. */;
        }
        data.SetTag(HttpTelemetryPrivacyPolicy.MethodTag, method) /* KLD0041: Publish the bounded normalized HTTP method string through the native Activity object-valued tag API. */;
        if (status is int and >= HttpTelemetryPrivacyPolicy.MinimumStatus and <= HttpTelemetryPrivacyPolicy.MaximumStatus)
        {
            data.SetTag(HttpTelemetryPrivacyPolicy.StatusTag, status) /* KLD0041: Publish the original validated status box through the native Activity object-valued tag API. */;
        }
        if (protocol is not null)
        {
            data.SetTag(HttpTelemetryPrivacyPolicy.ProtocolTag, protocol) /* KLD0041: Publish the bounded normalized HTTP protocol string through the native Activity object-valued tag API. */;
        }
        return true;
    }
    private static void Suppress(Activity data, string reason)
    {
        data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        Suppressed.Add(HttpTelemetryPrivacyPolicy.CounterIncrement, (new KeyValuePair<string, object?>(HttpTelemetryPrivacyPolicy.ReasonTag, reason) /* KLD0041: The native metrics tag pair carries this closed suppression-reason string without scalar boxing or caller payloads. */)) /* KLD0041: Emit one HTTP privacy suppression count with the native metrics object-valued pair containing only the closed reason string. */;
    }
}
