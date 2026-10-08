using KeyLoad.ServiceDefaults.Features.Authorization.Configuration;
using KeyLoad.ServiceDefaults.Features.Authorization.Contracts;
using KeyLoad.ServiceDefaults.Features.Authorization.Diagnostics;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Configuration;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Contracts;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace KeyLoad.ServiceDefaults;

/// <summary>Provides shared service-discovery, health-check, and telemetry defaults.</summary>
public static class KeyLoadServiceDefaultsExtensions
{
    private const string SelfHealthCheckName = "self";
    private const string LiveHealthTag = "live";
    private const string HealthRoutePrefix = "/health";
    private const string SiloDiscoveryRoute = "/internal/silo";
    private const string LiveHealthRoute = "/health/live";
    private const string OtlpExporterEndpointConfigurationKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Adds the shared service discovery, health check, and telemetry registrations.</summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The same builder, for continued composition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    [KeyLoad.ConfigurationBinding]
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
        builder.Services.AddHealthChecks().AddCheck(SelfHealthCheckName, () => HealthCheckResult.Healthy(), [LiveHealthTag]);
        builder.Services.AddOptions<OrleansTelemetryOptions>()
            .Bind(builder.Configuration.GetSection(OrleansTelemetryOptions.SectionName))
            .Validate(options => options.IsValid(), OrleansTelemetryOptions.ValidationMessage)
            .ValidateOnStart();
        builder.Services.AddOptions<HttpTelemetryPrivacyOptions>()
            .Bind(builder.Configuration.GetSection(HttpTelemetryPrivacyOptions.SectionName))
            .Validate(options => options.IsValid(), HttpTelemetryPrivacyOptions.ValidationMessage)
            .ValidateOnStart();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation()
                .AddMeter(OrleansTelemetryPolicy.OrleansMeterName, OrleansTelemetryPolicy.PrivacyMeterName, HttpTelemetryPrivacyPolicy.PrivacyMeter)
                .AddView(OrleansTelemetryPrivacyProcessor.ConfigureMetricView)
                .AddView(HttpTelemetryPrivacyProcessor.ConfigureMetricView)
                .SetExemplarFilter(ExemplarFilterType.AlwaysOff))
            .WithTracing(traces => traces.AddAspNetCoreInstrumentation(options => options.Filter = context =>
                !context.Request.Path.StartsWithSegments(HealthRoutePrefix, StringComparison.OrdinalIgnoreCase)
                && !context.Request.Path.StartsWithSegments(SiloDiscoveryRoute, StringComparison.OrdinalIgnoreCase))
                .AddHttpClientInstrumentation()
                .AddSource(OrleansTelemetryPolicy.ApplicationActivitySourceName,
                    OrleansTelemetryPolicy.LifecycleActivitySourceName)
                .AddProcessor(services => new OrleansTelemetryPrivacyProcessor(
                    services.GetRequiredService<IOptions<OrleansTelemetryOptions>>()))
                .AddProcessor(services => new HttpTelemetryPrivacyProcessor(
                    services.GetRequiredService<IOptions<HttpTelemetryPrivacyOptions>>())));
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = false;
            options.IncludeScopes = false;
            options.AddProcessor(new RuntimeLogPrivacyProcessor());
        });
        if (!string.IsNullOrEmpty(builder.Configuration[OtlpExporterEndpointConfigurationKey]))
        {
            builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddOtlpExporter()).WithTracing(t => t.AddOtlpExporter());
            builder.Logging.AddOpenTelemetry(options => options.AddOtlpExporter());
        }
        return builder;
    }

    /// <summary>Maps the shared live health-check endpoint.</summary>
    /// <param name="app">The web application to configure.</param>
    /// <returns>The same application, for continued composition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapHealthChecks(LiveHealthRoute, new() { Predicate = check => check.Tags.Contains(LiveHealthTag) });
        return app;
    }
}
