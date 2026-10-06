using System.Diagnostics;
using KeyLoad.Orleans;
using KeyLoad.ServiceDefaults;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class OrleansRuntimeTelemetryFixture : IAsyncDisposable
{
    private readonly IHost host;
    private readonly ActivitySource parentSource;

    private OrleansRuntimeTelemetryFixture(IHost host, OrleansActivityCaptureExporter activities,
        OrleansMetricCaptureExporter metrics, OrleansTelemetryCaptureOptions captureOptions)
    {
        this.host = host;
        Activities = activities;
        Metrics = metrics;
        parentSource = new(OrleansRuntimeTelemetryTokens.ParentSource);
        Options = host.Services.GetRequiredService<IOptions<OrleansTelemetryOptions>>().Value;
        CaptureOptions = captureOptions;
    }

    internal OrleansActivityCaptureExporter Activities { get; }
    internal OrleansMetricCaptureExporter Metrics { get; }
    internal OrleansTelemetryOptions Options { get; }
    internal OrleansTelemetryCaptureOptions CaptureOptions { get; }

    internal static async Task<OrleansRuntimeTelemetryFixture> StartAsync()
    {
        var activities = new OrleansActivityCaptureExporter();
        var metrics = new OrleansMetricCaptureExporter();
        var captureDefaults = new OrleansTelemetryCaptureOptions();
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddOptions<OrleansTelemetryCaptureOptions>()
            .Configure(options =>
            {
                options.MaximumRecords = captureDefaults.MaximumRecords;
                options.FlushTimeoutMilliseconds = captureDefaults.FlushTimeoutMilliseconds;
            })
            .Validate(options => options.IsValid(), OrleansTelemetryCaptureOptions.ValidationMessage)
            .ValidateOnStart();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(OrleansRuntimeTelemetryTokens.ParentSource)
                .AddProcessor(new SimpleActivityExportProcessor(activities)))
            .WithMetrics(meter => meter.AddReader(new PeriodicExportingMetricReader(metrics,
                exportIntervalMilliseconds: OrleansRuntimeTelemetryTokens.ExportIntervalMilliseconds)));
        var host = builder.Build();
        try
        {
            var captureOptions = host.Services.GetRequiredService<IOptions<OrleansTelemetryCaptureOptions>>();
            activities.Configure(captureOptions);
            metrics.Configure(captureOptions);
            await host.StartAsync();
            return new(host, activities, metrics, captureOptions.Value);
        }
        catch (Exception startupFailure)
        {
            await DisposeAfterStartupFailureAsync(host, startupFailure).ConfigureAwait(false);
            throw;
        }
    }

    internal Activity? StartParentActivity()
        => parentSource.StartActivity(OrleansRuntimeTelemetryTokens.ParentOperation);

    internal void Flush()
    {
        var tracing = host.Services.GetRequiredService<TracerProvider>();
        var metrics = host.Services.GetRequiredService<MeterProvider>();
        if (!tracing.ForceFlush(CaptureOptions.FlushTimeoutMilliseconds))
        {
            throw new InvalidOperationException("The telemetry trace provider did not flush before the test deadline.");
        }

        if (!metrics.ForceFlush(CaptureOptions.FlushTimeoutMilliseconds))
        {
            throw new InvalidOperationException("The telemetry metric provider did not flush before the test deadline.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Flush();
        }
        finally
        {
            try
            {
                using var timeout = new CancellationTokenSource(CaptureOptions.FlushTimeoutMilliseconds);
                await host.StopAsync(timeout.Token);
            }
            finally
            {
                try
                {
                    await host.DisposeAsync();
                }
                finally
                {
                    parentSource.Dispose();
                }
            }
        }
    }

    private static async Task DisposeAfterStartupFailureAsync(IHost failedHost, Exception startupFailure)
    {
        try
        {
            await failedHost.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupFailure) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanupFailure))
        {
            throw new AggregateException(startupFailure, cleanupFailure);
        }
        catch (Exception cleanupFailure) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanupFailure))
        {
            throw new AggregateException(startupFailure, cleanupFailure);
        }
    }
}
