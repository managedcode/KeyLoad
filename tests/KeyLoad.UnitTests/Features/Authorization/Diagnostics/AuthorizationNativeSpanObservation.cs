using System.Diagnostics;
using KeyLoad.ServiceDefaults.Features.Authorization.Configuration;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class AuthorizationNativeSpanObservation(IOptions<AuthorizationTelemetryCaptureOptions> options)
    : BaseProcessor<Activity>
{
    internal HttpTelemetryPrivacyOptions Privacy { get; set; } = new();
    internal AuthorizationCaptureBank<AuthorizationNativeSpanState> Bank { get; } = new(options);
    public override void OnEnd(Activity activity)
    {
        if (activity.Source.Name is not (AuthorizationTelemetryTestProtocol.HttpClientSource or AuthorizationTelemetryTestProtocol.HttpServerSource))
        { return; }
        var parents = Parents(activity, Privacy.MaximumParentLinks);
        Bank.Add(new(activity.Source.Name, activity.Recorded, activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(), activity.ParentSpanId.ToHexString(),
            activity.Events.Take(options.Value.MaximumRecords).Select(static item => item.Name).ToArray(),
            activity.Links.Take(options.Value.MaximumRecords).Select(static link => new AuthorizationNativeLinkState(
                link.Context.TraceId.ToHexString(), link.Context.SpanId.ToHexString(), link.Context.TraceState,
                link.Tags?.Take(AuthorizationTelemetryTestProtocol.CatalogCount).Any() ?? false)).ToArray(),
            parents.Length, parents,
            parents.Length <= Privacy.MaximumParentLinks ? activity.Baggage.Take(Privacy.MaximumBaggageItems + AuthorizationTelemetryTestProtocol.CatalogCount).ToArray() : [],
            activity.TagObjects.Take(Privacy.MaximumTags + AuthorizationTelemetryTestProtocol.CatalogCount)
                .Select(static tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray(),
            Privacy.MaximumParentLinks, Privacy.MaximumBaggageItems, Privacy.MaximumTags));
    }
    private static string[] Parents(Activity activity, int maximum)
    {
        var parents = new List<string>();
        for (var parent = activity.Parent; parent is not null; parent = parent.Parent)
        {
            parents.Add(parent.OperationName);
            if (parents.Count > maximum)
            { break; }
        }
        return [.. parents];
    }
}
