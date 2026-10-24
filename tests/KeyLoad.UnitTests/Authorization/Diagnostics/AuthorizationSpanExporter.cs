using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace KeyLoad.UnitTests.Authorization;

internal sealed class AuthorizationSpanExporter(IOptions<AuthorizationTelemetryCaptureOptions> options)
    : BaseExporter<Activity>
{
    internal AuthorizationCaptureBank<AuthorizationSpanCapture> Bank { get; } = new(options);
    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            Bank.Add(new(activity.Source.Name, activity.OperationName, activity.DisplayName,
                activity.Kind, activity.Status, activity.StatusDescription, activity.TraceStateString,
                activity.TraceId.ToHexString(), activity.SpanId.ToHexString(), activity.ParentSpanId.ToHexString(),
                activity.Duration, activity.TagObjects.Select(static x => new KeyValuePair<string, string?>(x.Key, x.Value?.ToString())).ToArray(),
                activity.Baggage.ToArray(), activity.Events.Select(static x => x.Name + JsonSerializer.Serialize(x.Tags.ToArray(), JsonDefaults.Options)).ToArray(),
                activity.Links.Select(static link => new AuthorizationLinkCapture(link.Context.TraceId.ToHexString(),
                    link.Context.SpanId.ToHexString(), link.Context.TraceState, link.Context.IsRemote, link.Context.TraceFlags,
                    link.Tags?.Select(static tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray() ?? [])).ToArray()));
        }
        return ExportResult.Success;
    }
}
