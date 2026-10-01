using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace KeyLoad.ServiceDefaults;

public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation())
            .WithTracing(traces => traces.AddAspNetCoreInstrumentation(options => options.Filter = context =>
                !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/raft"))
                .AddHttpClientInstrumentation());
        builder.Logging.AddOpenTelemetry(options => { options.IncludeFormattedMessage = true; options.IncludeScopes = true; });
        if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddOtlpExporter()).WithTracing(t => t.AddOtlpExporter());
            builder.Logging.AddOpenTelemetry(options => options.AddOtlpExporter());
        }
        return builder;
    }
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new() { Predicate = check => check.Tags.Contains("live") });
        return app;
    }
}
