using System.Diagnostics;
using System.Diagnostics.Metrics;
using KeyLoad.ServiceDefaults.Features.Authorization.Configuration;
using KeyLoad.ServiceDefaults.Features.Authorization.Contracts;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KeyLoad.ServiceDefaults.Features.Authorization.Diagnostics;

internal sealed class HttpTelemetryPrivacyProcessor(IOptions<HttpTelemetryPrivacyOptions> options) : BaseProcessor<Activity>
{
    private const int InitialParentCount = 0;
    private static readonly Meter PrivacyMeter = new(HttpTelemetryPrivacyPolicy.PrivacyMeter);
    private static readonly Counter<long> Suppressed = PrivacyMeter.CreateCounter<long>(HttpTelemetryPrivacyPolicy.SuppressedCounter);
    public override void OnStart(Activity data)
    {
        if (HttpTelemetryPrivacyPolicy.IsHttp(data.Source.Name))
        { data.TraceStateString = null; }
    }
    public override void OnEnd(Activity data)
    {
        if (!HttpTelemetryPrivacyPolicy.IsHttp(data.Source.Name) || !data.Recorded)
        { return; }
        data.TraceStateString = null;
        var linkReason = HttpTelemetryPrivacyPolicy.LinkSuppressionReason(data);
        if (linkReason is not null)
        { Suppress(data, linkReason); return; }
        if (data.Events.Any())
        { Suppress(data, HttpTelemetryPrivacyPolicy.UnsafeStateReason); return; }
        if (!ClearBaggage(data) || !ScrubTags(data))
        { Suppress(data, HttpTelemetryPrivacyPolicy.ExcessStateReason); return; }
        data.DisplayName = data.Source.Name == HttpTelemetryPrivacyPolicy.ServerSource
            ? HttpTelemetryPrivacyPolicy.ServerDisplay : HttpTelemetryPrivacyPolicy.ClientDisplay;
        data.SetStatus(data.Status, null);
    }
    internal static MetricStreamConfiguration ConfigureMetricView(Instrument instrument)
        => instrument.Meter.Name is HttpTelemetryPrivacyPolicy.ServerMeter or HttpTelemetryPrivacyPolicy.ClientMeter
            ? new MetricStreamConfiguration { TagKeys = [] } : null!;
    private bool ClearBaggage(Activity data)
    {
        if (!HasBoundedAncestry(data))
        { return false; }
        var keys = new List<string>(options.Value.MaximumBaggageItems);
        foreach (var item in data.Baggage)
        {
            if (keys.Count == options.Value.MaximumBaggageItems)
            { return false; }
            keys.Add(item.Key);
        }
        foreach (var key in keys)
        { data.SetBaggage(key, null); }
        using var remaining = data.Baggage.GetEnumerator();
        return !remaining.MoveNext();
    }
    private bool HasBoundedAncestry(Activity data)
    {
        var examined = InitialParentCount;
        for (var parent = data.Parent; parent is not null; parent = parent.Parent)
        {
            if (++examined > options.Value.MaximumParentLinks)
            { return false; }
        }
        return true;
    }
    private bool ScrubTags(Activity data)
    {
        var keys = new List<string>(options.Value.MaximumTags);
        foreach (var item in data.TagObjects)
        {
            if (keys.Count == options.Value.MaximumTags)
            { return false; }
            keys.Add(item.Key);
        }
        var method = HttpTelemetryPrivacyPolicy.NormalizeMethod(data.GetTagItem(HttpTelemetryPrivacyPolicy.MethodTag));
        var status = data.GetTagItem(HttpTelemetryPrivacyPolicy.StatusTag);
        var protocol = HttpTelemetryPrivacyPolicy.NormalizeProtocol(data.GetTagItem(HttpTelemetryPrivacyPolicy.ProtocolTag));
        foreach (var key in keys)
        { data.SetTag(key, null); }
        data.SetTag(HttpTelemetryPrivacyPolicy.MethodTag, method);
        if (status is int code and >= HttpTelemetryPrivacyPolicy.MinimumStatus and <= HttpTelemetryPrivacyPolicy.MaximumStatus)
        { data.SetTag(HttpTelemetryPrivacyPolicy.StatusTag, code); }
        if (protocol is not null)
        { data.SetTag(HttpTelemetryPrivacyPolicy.ProtocolTag, protocol); }
        return true;
    }
    private static void Suppress(Activity data, string reason)
    {
        data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        Suppressed.Add(HttpTelemetryPrivacyPolicy.CounterIncrement, new KeyValuePair<string, object?>(HttpTelemetryPrivacyPolicy.ReasonTag, reason));
    }
}
