using System.Diagnostics;
using System.Text.Json;
using KeyLoad.ServiceDefaults.Features.Authorization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationTelemetryAssertions
{
    internal static async Task CatalogAsync(AdminResourcesPage page, PartitionRef partition, long position)
    {
        await Assert.That(page.Items.Length).IsEqualTo(AuthorizationTelemetryTestProtocol.CatalogCount);
        var item = page.Items.Single();
        await Assert.That(item.Name).IsEqualTo(AuthorizationTelemetryTestProtocol.Resource);
        await Assert.That(item.Kind).IsEqualTo(ResourceKind.Collection);
        await Assert.That(item.TransactionDomainId).IsEqualTo(partition.TransactionDomainId);
        await Assert.That(item.SchemaVersion).IsEqualTo(AuthorizationTelemetryTestProtocol.SchemaVersion);
        await Assert.That(item.IndexCount).IsEqualTo(AuthorizationTelemetryTestProtocol.IndexCount);
        await Assert.That(item.Paused).IsFalse();
        await Assert.That(page.NextAfterName).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(position);
    }
    internal static async Task ExportsAsync(AuthorizationTelemetryHttpFixture fixture, AuthorizationUnsafeSpanState state, string? suppressedTrace)
    {
        var nativeSpans = fixture.NativeSpans.Bank.Snapshot();
        var spans = fixture.Spans.Bank.Snapshot();
        var logs = fixture.Logs.Bank.Snapshot();
        var metrics = fixture.Metrics.Bank.Snapshot();
        var observation = fixture.Observations.Snapshot();
        var suppressed = fixture.Metrics.Suppressed.Snapshot();
        var serialized = JsonSerializer.Serialize(new { spans, logs, metrics, observation, suppressed }, JsonDefaults.Options);
        foreach (var canary in AuthorizationTelemetryTestProtocol.Canaries)
        { await Assert.That(serialized.Contains(canary, StringComparison.Ordinal)).IsFalse(); }
        var servers = spans.Where(static span => span.Source == AuthorizationTelemetryTestProtocol.HttpServerSource).ToArray();
        var clients = spans.Where(static span => span.Source == AuthorizationTelemetryTestProtocol.HttpClientSource).ToArray();
        await Assert.That(servers.Length).IsEqualTo(state is AuthorizationUnsafeSpanState.None or AuthorizationUnsafeSpanState.InheritedParentBaggage or AuthorizationUnsafeSpanState.ExcessParentLinks
            or AuthorizationUnsafeSpanState.ClientEvent or AuthorizationUnsafeSpanState.ClientTaggedLink or AuthorizationUnsafeSpanState.ClientTraceStateLink or AuthorizationUnsafeSpanState.ClientExtraLink
            ? AuthorizationTelemetryTestProtocol.RequestCount : AuthorizationTelemetryTestProtocol.HealthyCount);
        await Assert.That(clients.Length).IsEqualTo(state is AuthorizationUnsafeSpanState.InheritedParentBaggage or AuthorizationUnsafeSpanState.ExcessParentLinks
            or AuthorizationUnsafeSpanState.ClientEvent or AuthorizationUnsafeSpanState.ClientTaggedLink or AuthorizationUnsafeSpanState.ClientTraceStateLink or AuthorizationUnsafeSpanState.ClientExtraLink
            ? AuthorizationTelemetryTestProtocol.HealthyCount : AuthorizationTelemetryTestProtocol.RequestCount).Because(JsonSerializer.Serialize(new { exported = serialized, nativeSpans }, JsonDefaults.Options));
        await AuthorizationTelemetryNativeAssertions.SuppressionAsync(nativeSpans, suppressed, state, suppressedTrace);
        await AuthorizationTelemetryNativeAssertions.IdentityAsync(spans, nativeSpans);
        await SpansAsync(servers, clients, suppressedTrace);
        await LogsAsync(logs, servers, clients, suppressedTrace);
        await MetricsAsync(metrics);
        await ObservationAsync(observation);
    }
    private static async Task SpansAsync(AuthorizationSpanCapture[] servers, AuthorizationSpanCapture[] clients, string? suppressedTrace)
    {
        foreach (var server in servers)
        {
            await Assert.That(server.Display).IsEqualTo(HttpTelemetryPrivacyPolicy.ServerDisplay);
            await Assert.That(server.Kind).IsEqualTo(ActivityKind.Server);
            await Assert.That(server.Duration).IsGreaterThan(TimeSpan.Zero);
            await Assert.That(server.TraceId).IsNotEqualTo(default(ActivityTraceId).ToHexString());
            await Assert.That(clients.Any(client => client.TraceId == server.TraceId && client.SpanId == server.ParentSpanId)
                || (suppressedTrace is not null && server.TraceId == suppressedTrace)).IsTrue();
            await PrivateStateAsync(server);
        }
        foreach (var client in clients)
        {
            await Assert.That(client.Display).IsEqualTo(HttpTelemetryPrivacyPolicy.ClientDisplay);
            await Assert.That(client.Kind).IsEqualTo(ActivityKind.Client);
            await Assert.That(client.Duration).IsGreaterThan(TimeSpan.Zero);
            await PrivateStateAsync(client);
        }
        await Assert.That(clients.Any(client => client.Status == ActivityStatusCode.Error)
            || servers.Any(server => server.Status == ActivityStatusCode.Error)).IsTrue();
    }
    private static async Task PrivateStateAsync(AuthorizationSpanCapture span)
    {
        await Assert.That(span.StatusDetail).IsNull();
        await Assert.That(span.TraceState).IsNull();
        await Assert.That(span.Baggage).IsEmpty();
        await Assert.That(span.Events).IsEmpty();
        await Assert.That(span.Links.Length).IsEqualTo(span.Kind == ActivityKind.Client ? AuthorizationTelemetryTestProtocol.CatalogCount : AuthorizationTelemetryTestProtocol.IndexCount);
        foreach (var link in span.Links)
        {
            await Assert.That(span.Operation).IsEqualTo(HttpTelemetryPrivacyPolicy.ClientOperation);
            await Assert.That(link.TraceId).IsNotEqualTo(default(ActivityTraceId).ToHexString());
            await Assert.That(link.SpanId).IsNotEqualTo(default(ActivitySpanId).ToHexString());
            await Assert.That(link.TraceState).IsNull();
            await Assert.That(link.IsRemote).IsFalse();
            await Assert.That(link.TraceFlags is ActivityTraceFlags.None or ActivityTraceFlags.Recorded).IsTrue();
            await Assert.That(link.Tags).IsEmpty();
        }
        await Assert.That(span.Tags.All(static tag => tag.Key is HttpTelemetryPrivacyPolicy.MethodTag
            or HttpTelemetryPrivacyPolicy.StatusTag or HttpTelemetryPrivacyPolicy.ProtocolTag)).IsTrue();
        await Assert.That(span.Tags.Single(static tag => tag.Key == HttpTelemetryPrivacyPolicy.MethodTag).Value).IsEqualTo(HttpMethods.Post);
    }
    private static async Task LogsAsync(AuthorizationLogCapture[] logs, AuthorizationSpanCapture[] servers,
        AuthorizationSpanCapture[] clients, string? suppressedTrace)
    {
        var operationLogs = logs.Where(static log => log.EventId is AuthorizationTelemetryTestProtocol.DeniedEventId
            or AuthorizationTelemetryTestProtocol.HealthyEventId).ToArray();
        await Assert.That(operationLogs.Length).IsEqualTo(AuthorizationTelemetryTestProtocol.RequestCount);
        foreach (var log in operationLogs)
        {
            await Assert.That(log.Category).IsEqualTo(HttpTelemetryPrivacyPolicy.KeyLoadCategory);
            await Assert.That(log.Body).IsEqualTo(HttpTelemetryPrivacyPolicy.RuntimeBody);
            await Assert.That(log.Formatted).IsEqualTo(HttpTelemetryPrivacyPolicy.RuntimeBody);
            await Assert.That(log.EventName).IsNull();
            await Assert.That(log.Exception).IsNull();
            await Assert.That(log.TraceState).IsNull();
            await Assert.That(log.Attributes).IsEmpty();
            await Assert.That(log.Scopes).IsEmpty();
            await Assert.That(log.Timestamp).IsGreaterThan(default(DateTime));
            await Assert.That(clients.Any(client => client.TraceId == log.TraceId)
                || (suppressedTrace is not null && log.TraceId == suppressedTrace)).IsTrue();
        }
        var healthy = operationLogs.Single(static log => log.EventId == AuthorizationTelemetryTestProtocol.HealthyEventId);
        await Assert.That(servers.Any(span => span.SpanId == healthy.SpanId && span.TraceId == healthy.TraceId)).IsTrue();
        await Assert.That(operationLogs.Single(static log => log.EventId == AuthorizationTelemetryTestProtocol.DeniedEventId).Level).IsEqualTo(LogLevel.Error);
        await Assert.That(healthy.Level).IsEqualTo(LogLevel.Information);
    }
    private static async Task MetricsAsync(AuthorizationMetricCapture[] metrics)
    {
        foreach (var name in new[] { AuthorizationTelemetryTestProtocol.ServerDuration, AuthorizationTelemetryTestProtocol.ClientDuration })
        {
            var observed = metrics.Where(metric => metric.Name == name).ToArray();
            await Assert.That(observed.Length).IsGreaterThan(AuthorizationTelemetryTestProtocol.IndexCount);
            await Assert.That(observed.Max(static metric => metric.Count)).IsGreaterThanOrEqualTo((long)AuthorizationTelemetryTestProtocol.RequestCount);
            await Assert.That(observed.All(static metric => metric.Sum > AuthorizationTelemetryTestProtocol.IndexCount)).IsTrue();
            await Assert.That(observed.All(static metric => metric.Tags.Length == AuthorizationTelemetryTestProtocol.IndexCount)).IsTrue();
        }
    }
    private static async Task ObservationAsync(AdminHttpSnapshot observation)
    {
        await Assert.That(observation.CompletedRequests).IsEqualTo((long)AuthorizationTelemetryTestProtocol.RequestCount);
        await Assert.That(observation.FailedRequests).IsEqualTo((long)AuthorizationTelemetryTestProtocol.HealthyCount);
        await Assert.That(observation.ElapsedMilliseconds).IsGreaterThan((double)AuthorizationTelemetryTestProtocol.IndexCount);
        var failure = observation.RecentFailures.Single();
        await Assert.That(failure.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(failure.Route).IsEqualTo(AuthorizationTelemetryTestProtocol.Route);
        await Assert.That(failure.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(failure.Aborted).IsFalse();
    }
}
