using System.Diagnostics;
using System.Text.Json;
using KeyLoad.ServiceDefaults.Features.Authorization.Contracts;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace KeyLoad.UnitTests.Features.Authorization;

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
internal sealed class AuthorizationLogExporter(IOptions<AuthorizationTelemetryCaptureOptions> options)
    : BaseExporter<LogRecord>
{
    internal AuthorizationCaptureBank<AuthorizationLogCapture> Bank { get; } = new(options);
    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (var log in batch)
        {
            var scopes = new List<string>();
            log.ForEachScope(static (scope, target) => target.Add(scope.Scope?.ToString() ?? string.Empty), scopes);
            Bank.Add(new(log.CategoryName, log.Body, log.FormattedMessage, log.EventId.Id, log.EventId.Name,
                log.LogLevel, log.Timestamp, log.TraceId.ToHexString(), log.SpanId.ToHexString(), log.TraceState,
                log.Exception?.ToString(), log.Attributes?.Select(static x => new KeyValuePair<string, string?>(x.Key, x.Value?.ToString())).ToArray() ?? [],
                [.. scopes]));
        }
        return ExportResult.Success;
    }
}
internal sealed class AuthorizationMetricExporter(IOptions<AuthorizationTelemetryCaptureOptions> options)
    : BaseExporter<Metric>
{
    internal AuthorizationCaptureBank<AuthorizationMetricCapture> Bank { get; } = new(options);
    internal AuthorizationCaptureBank<AuthorizationSuppressionCapture> Suppressed { get; } = new(options);
    public override ExportResult Export(in Batch<Metric> batch)
    {
        foreach (var metric in batch)
        {
            if (metric.Name == HttpTelemetryPrivacyPolicy.SuppressedCounter)
            {
                CaptureSuppression(metric);
                continue;
            }
            if (metric.Name is not (AuthorizationTelemetryTestProtocol.ServerDuration or AuthorizationTelemetryTestProtocol.ClientDuration))
            { continue; }
            foreach (var point in metric.GetMetricPoints())
            {
                var tags = new List<KeyValuePair<string, string?>>();
                foreach (var tag in point.Tags)
                { tags.Add(new(tag.Key, tag.Value?.ToString())); }
                Bank.Add(new(metric.MeterName, metric.Name, point.GetHistogramCount(), point.GetHistogramSum(), [.. tags]));
            }
        }
        return ExportResult.Success;
    }
    private void CaptureSuppression(Metric metric)
    {
        foreach (var point in metric.GetMetricPoints())
        {
            var tags = new List<KeyValuePair<string, string?>>();
            foreach (var tag in point.Tags)
            { tags.Add(new(tag.Key, tag.Value?.ToString())); }
            Suppressed.Add(new(point.GetSumLong(), [.. tags]));
        }
    }
}
