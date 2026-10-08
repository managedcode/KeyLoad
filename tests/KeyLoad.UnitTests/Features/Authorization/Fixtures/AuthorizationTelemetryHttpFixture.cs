using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class AuthorizationTelemetryHttpFixture : IDisposable, IAsyncDisposable
{
    private readonly TimeProvider clock = TimeProvider.System;
    private readonly IOptions<AuthorizationTelemetryCaptureOptions> options;
    private bool disposed;
    private WebApplication? app;
    internal AuthorizationUnsafeSpanState UnsafeState { get; set; }
    private HttpClient? client;
    private readonly SemaphoreSlim completedRequests = new(AuthorizationTelemetryTestProtocol.IndexCount);
    internal AuthorizationTelemetryHttpFixture()
    {
        var policy = new AuthorizationTelemetryCaptureOptions();
        if (!policy.IsValid())
        { throw new InvalidOperationException(AuthorizationTelemetryTestProtocol.CaptureFailure); }
        options = Options.Create(policy);
        Spans = new(options);
        Logs = new(options);
        Metrics = new(options);
        NativeSpans = new(options);
    }
    internal AuthorizationNativeSpanObservation NativeSpans { get; }
    internal AuthorizationSpanExporter Spans { get; }
    internal AuthorizationLogExporter Logs { get; }
    internal AuthorizationMetricExporter Metrics { get; }
    internal AdminHttpMetrics Observations { get; } = new(UnitAdminObservationOptions.Execution());
    internal TestDatabase Database { get; } = new();
    internal HttpClient Client => client ?? throw new InvalidOperationException(nameof(Client));
    internal CancellationTokenSource Deadline() => new(TimeSpan.FromMilliseconds(options.Value.FlushTimeoutMilliseconds), clock);

    internal async Task InitializeAsync()
    {
        Seed();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddProcessor(new AuthorizationUnsafeClientSpanProcessor(() => UnsafeState)).AddProcessor(NativeSpans));
        builder.AddServiceDefaults();
        builder.Services.AddSingleton(Observations);
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new BatchActivityExportProcessor(Spans,
                maxQueueSize: options.Value.MaximumRecords,
                scheduledDelayMilliseconds: options.Value.ExportIntervalMilliseconds,
                exporterTimeoutMilliseconds: options.Value.FlushTimeoutMilliseconds,
                maxExportBatchSize: options.Value.MaximumRecords)))
            .WithMetrics(metrics => metrics.AddReader(new PeriodicExportingMetricReader(Metrics,
                exportIntervalMilliseconds: options.Value.ExportIntervalMilliseconds)));
        builder.Logging.AddOpenTelemetry(logs => logs.AddProcessor(new SimpleLogRecordExportProcessor(Logs)));
        app = builder.Build();
        NativeSpans.Privacy = app.Services.GetRequiredService<IOptions<KeyLoad.ServiceDefaults.Features.Authorization.Configuration.HttpTelemetryPrivacyOptions>>().Value;
        app.UseMiddleware<AdminHttpMetricsMiddleware>();
        var endpoint = new AuthorizationTelemetryEndpoint(Database, clock, () => UnsafeState, () => completedRequests.Release());
        app.MapPost(AuthorizationTelemetryTestProtocol.Route, endpoint.ExecuteAsync);
        using var deadline = Deadline();
        await app.StartAsync(deadline.Token);
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        client = new() { BaseAddress = new Uri(addresses.Addresses.Single()), Timeout = Timeout.InfiniteTimeSpan };
    }
    private void Seed()
    {
        Database.Configure(AuthorizationTelemetryTestProtocol.Resource, ResourceKind.Collection);
        Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(
            AuthorizationTelemetryTestProtocol.Member, Database.Partition.TenantId,
            [new(Database.Partition.DatabaseId, AuthorizationTelemetryTestProtocol.Resource, Capability.All)], []))).Get<PrincipalRecord>();
        var configured = Database.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(DatabaseEngine.Credential(
            AuthorizationTelemetryTestProtocol.Member, AuthorizationTelemetryTestProtocol.Member,
            AuthorizationTelemetryTestProtocol.MemberKey))).Get<bool>();
        if (!configured)
        { throw new InvalidOperationException(AuthorizationTelemetryTestProtocol.CaptureFailure); }
    }
    internal void Flush()
    {
        if (app is null)
        { return; }
        var tracing = app.Services.GetRequiredService<TracerProvider>();
        var metrics = app.Services.GetRequiredService<MeterProvider>();
        var logging = app.Services.GetRequiredService<LoggerProvider>();
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => FlushProvider(tracing.ForceFlush(options.Value.FlushTimeoutMilliseconds)), failures);
        ServerFailureObserver.Observe(() => FlushProvider(metrics.ForceFlush(options.Value.FlushTimeoutMilliseconds)), failures);
        ServerFailureObserver.Observe(() => FlushProvider(logging.ForceFlush(options.Value.FlushTimeoutMilliseconds)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal async Task JoinProducersAsync(CancellationToken cancellationToken)
    {
        for (var index = AuthorizationTelemetryTestProtocol.IndexCount; index < AuthorizationTelemetryTestProtocol.RequestCount; index++)
        { await completedRequests.WaitAsync(cancellationToken); }
    }
    internal async Task AwaitExportsAsync(AuthorizationUnsafeSpanState state, CancellationToken cancellationToken)
    {
        var expected = state is AuthorizationUnsafeSpanState.None or AuthorizationUnsafeSpanState.InheritedParentBaggage or AuthorizationUnsafeSpanState.ExcessParentLinks
            or AuthorizationUnsafeSpanState.ClientEvent or AuthorizationUnsafeSpanState.ClientTaggedLink or AuthorizationUnsafeSpanState.ClientTraceStateLink or AuthorizationUnsafeSpanState.ClientExtraLink
            ? AuthorizationTelemetryTestProtocol.RequestCount : AuthorizationTelemetryTestProtocol.HealthyCount;
        Flush();
        while (Spans.Bank.Snapshot().Count(static span => span.Source == AuthorizationTelemetryTestProtocol.HttpServerSource) < expected)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds), clock, cancellationToken);
            Flush();
        }
        Flush();
    }
    private static void FlushProvider(bool success)
    {
        if (!success)
        { throw new InvalidOperationException(AuthorizationTelemetryTestProtocol.FlushFailure); }
    }
    internal async Task RunAsync(Func<AuthorizationTelemetryHttpFixture, Task> flow)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await InitializeAsync();
            await flow(this);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        { return; }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Flush, failures);
        if (app is { } owned)
        {
            using var timeout = Deadline();
            await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(timeout.Token), failures);
        }
        ServerFailureObserver.Observe(Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        var failures = new List<Exception>();
        try
        { client?.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { ((IDisposable?)app)?.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { completedRequests.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { Database.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
internal sealed record AuthorizationTelemetryFailure(ErrorCode Code, string? Detail);
