using KeyLoad.Server.Authorization.Contracts;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KeyLoad.UnitTests.Authorization;

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
            {
                continue;
            }
            foreach (var point in metric.GetMetricPoints())
            {
                var tags = new List<KeyValuePair<string, string?>>();
                foreach (var tag in point.Tags)
                {
                    tags.Add(new(tag.Key, tag.Value?.ToString()));
                }
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
            {
                tags.Add(new(tag.Key, tag.Value?.ToString()));
            }
            Suppressed.Add(new(point.GetSumLong(), [.. tags]));
        }
    }
}
