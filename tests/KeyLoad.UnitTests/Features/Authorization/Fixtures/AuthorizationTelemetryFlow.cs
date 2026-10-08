using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OpenTelemetry;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationTelemetryFlow
{
    internal static async Task ExecuteAsync(AuthorizationTelemetryHttpFixture fixture, AuthorizationUnsafeSpanState state)
    {
        var partition = fixture.Database.Partition;
        var position = fixture.Database.Database.LastApplied;
        using var deadline = fixture.Deadline();
        fixture.UnsafeState = state;
        using var connectionState = new AuthorizationConnectionTraceState(() => fixture.UnsafeState);
        var request = new AdminResourcesRequest(partition.TenantId, partition.DatabaseId,
            AuthorizationTelemetryTestProtocol.BodyCanary);
        string? suppressedTrace;
        var ambient = Activity.Current;
        using (var callers = new AuthorizationTelemetryCallerActivities(state))
        {
            await AuthorizationTelemetryNativeAssertions.CallerAsync(callers);
            var caller = callers.Current;
            await Assert.That(caller!.Recorded).IsTrue();
            var unsafeParent = state is AuthorizationUnsafeSpanState.InheritedParentBaggage or AuthorizationUnsafeSpanState.ExcessParentLinks;
            if (unsafeParent)
            { caller.AddBaggage(AuthorizationTelemetryTestProtocol.BaggageKey, AuthorizationTelemetryTestProtocol.BaggageCanary); }
            suppressedTrace = unsafeParent || state is AuthorizationUnsafeSpanState.ClientEvent or AuthorizationUnsafeSpanState.ClientTaggedLink
                or AuthorizationUnsafeSpanState.ClientTraceStateLink or AuthorizationUnsafeSpanState.ClientExtraLink ? caller.TraceId.ToHexString() : null;
            using (var denied = await SendAsync(fixture, AuthorizationTelemetryTestProtocol.MemberKey, request, deadline.Token))
            {
                await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
                var error = await denied.Content.ReadFromJsonAsync<AuthorizationTelemetryFailure>(JsonDefaults.Options, deadline.Token);
                await Assert.That(error!.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(error.Detail).IsEqualTo(AuthorizationTelemetryTestProtocol.ScopeDenial);
            }
            if (unsafeParent)
            { await Assert.That(caller.GetBaggageItem(AuthorizationTelemetryTestProtocol.BaggageKey)).IsEqualTo(AuthorizationTelemetryTestProtocol.BaggageCanary); }
        }
        await Assert.That(ReferenceEquals(Activity.Current, ambient)).IsTrue();
        await Assert.That(fixture.Database.Database.LastApplied).IsEqualTo(position);
        fixture.UnsafeState = AuthorizationUnsafeSpanState.None;
        using (var healthyCaller = new AuthorizationTelemetryCallerActivities(AuthorizationUnsafeSpanState.None))
        {
            await AuthorizationTelemetryNativeAssertions.CallerAsync(healthyCaller);
            using var healthy = await SendAsync(fixture, AuthorizationTelemetryTestProtocol.AdministratorKey,
                request with { AfterName = null }, deadline.Token);
            await Assert.That(healthyCaller.Current!.Recorded).IsTrue();
            await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var catalog = await healthy.Content.ReadFromJsonAsync<AdminResourcesPage>(JsonDefaults.Options, deadline.Token);
            await AuthorizationTelemetryAssertions.CatalogAsync(catalog!, fixture.Database.Partition, position);
        }
        await Assert.That(ReferenceEquals(Activity.Current, ambient)).IsTrue();
        await Assert.That(fixture.Database.Database.LastApplied).IsEqualTo(position);
        await fixture.JoinProducersAsync(deadline.Token);
        await fixture.AwaitExportsAsync(state, deadline.Token);
        await AuthorizationTelemetryAssertions.ExportsAsync(fixture, state, suppressedTrace);
    }
    private static Task<HttpResponseMessage> SendAsync(AuthorizationTelemetryHttpFixture fixture, string secret,
        AdminResourcesRequest payload, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AuthorizationTelemetryTestProtocol.Target);
        request.Headers.ConnectionClose = fixture.UnsafeState == AuthorizationUnsafeSpanState.ClientTraceStateLink;
        request.Headers.Authorization = new AuthenticationHeaderValue(AuthorizationTelemetryTestProtocol.Bearer, secret);
        request.Headers.Add(AuthorizationTelemetryTestProtocol.PrivateHeader, AuthorizationTelemetryTestProtocol.HeaderCanary);
        request.Content = JsonContent.Create(payload, options: JsonDefaults.Options);
        return SendAndDisposeAsync(fixture, request, cancellationToken);
    }
    private static async Task<HttpResponseMessage> SendAndDisposeAsync(AuthorizationTelemetryHttpFixture fixture,
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            await Assert.That(Sdk.SuppressInstrumentation).IsFalse();
            return await fixture.Client.SendAsync(request, cancellationToken);
        }
    }
}
