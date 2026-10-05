using KeyLoad.ServiceDefaults;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class OrleansRuntimeTelemetryFixture : IAsyncDisposable
{
    private readonly IHost host;
    private readonly ActivitySource parentSource;

    private OrleansRuntimeTelemetryFixture(IHost host, OrleansActivityCaptureExporter activities,
        OrleansMetricCaptureExporter metrics)
    {
        this.host = host;
        Activities = activities;
        Metrics = metrics;
        parentSource = new(OrleansRuntimeTelemetryTokens.ParentSource);
        Options = host.Services.GetRequiredService<IOptions<OrleansTelemetryOptions>>().Value;
    }

    internal OrleansActivityCaptureExporter Activities { get; }
    internal OrleansMetricCaptureExporter Metrics { get; }
    internal OrleansTelemetryOptions Options { get; }

    internal static async Task<OrleansRuntimeTelemetryFixture> StartAsync()
    {
        var activities = new OrleansActivityCaptureExporter();
        var metrics = new OrleansMetricCaptureExporter();
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddOptions<OrleansTelemetryCaptureOptions>()
            .Validate(options => options.IsValid(), OrleansTelemetryCaptureOptions.ValidationMessage)
            .ValidateOnStart();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(OrleansRuntimeTelemetryTokens.ParentSource)
                .AddProcessor(activities))
            .WithMetrics(meter => meter.AddReader(new PeriodicExportingMetricReader(metrics,
                exportIntervalMilliseconds: OrleansRuntimeTelemetryTokens.ExportIntervalMilliseconds)));
        var host = builder.Build();
        try
        {
            var captureOptions = host.Services.GetRequiredService<IOptions<OrleansTelemetryCaptureOptions>>().Value;
            activities.Configure(captureOptions);
            metrics.Configure(captureOptions);
            await host.StartAsync();
            return new(host, activities, metrics);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    internal Activity? StartParentActivity()
        => parentSource.StartActivity(OrleansRuntimeTelemetryTokens.ParentOperation);

    internal void Flush()
    {
        var tracing = host.Services.GetRequiredService<TracerProvider>();
        var metrics = host.Services.GetRequiredService<MeterProvider>();
        if (!tracing.ForceFlush())
        {
            throw new InvalidOperationException("The telemetry trace provider did not flush before the test deadline.");
        }

        if (!metrics.ForceFlush())
        {
            throw new InvalidOperationException("The telemetry metric provider did not flush before the test deadline.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Flush();
        await host.StopAsync();
        await host.DisposeAsync();
        parentSource.Dispose();
    }
}
