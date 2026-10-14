using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationMalformedHttpExportFlow
{
    internal static async Task ExecuteAsync(AuthorizationTelemetryHttpFixture fixture)
    {
        var original = AuthorizationTelemetryPolicyRepair.Capture(fixture.Database);
        var partition = fixture.Database.Partition;
        var payload = new AdminResourcesRequest(partition.TenantId, partition.DatabaseId,
            AuthorizationTelemetryTestProtocol.BodyCanary);
        using var deadline = fixture.Deadline();
        using (var caller = new AuthorizationTelemetryCallerActivities(AuthorizationUnsafeSpanState.None))
        {
            await AuthorizationTelemetryNativeAssertions.CallerAsync(caller);
            using var invalid = await SendAsync(fixture, payload, malformed: true, deadline.Token);
            await AuthorizationMalformedHttpExportAssertions.ProblemAsync(invalid, deadline.Token);
        }
        await AuthorizationTelemetryPolicyRepair.UnchangedAsync(fixture.Database, original);
        var position = await AuthorizationTelemetryPolicyRepair.RepairAsync(fixture.Database);
        var repaired = AuthorizationTelemetryPolicyRepair.Capture(fixture.Database);
        using (var caller = new AuthorizationTelemetryCallerActivities(AuthorizationUnsafeSpanState.None))
        {
            await AuthorizationTelemetryNativeAssertions.CallerAsync(caller);
            using var healthy = await SendAsync(fixture, payload with
            {
                AfterName = null
            }, malformed: false, deadline.Token);
            await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var catalog = await healthy.Content.ReadFromJsonAsync<AdminResourcesPage>(JsonDefaults.Options, deadline.Token);
            await AuthorizationTelemetryAssertions.CatalogAsync(catalog!, partition, position);
        }
        await AuthorizationTelemetryPolicyRepair.UnchangedAsync(fixture.Database, repaired);
        await fixture.JoinProducersAsync(deadline.Token);
        await fixture.AwaitExportsAsync(AuthorizationUnsafeSpanState.None, deadline.Token);
        await AuthorizationTelemetryAssertions.ExportsAsync(fixture, AuthorizationUnsafeSpanState.None,
            suppressedTrace: null, malformedJson: true);
    }

    private static async Task<HttpResponseMessage> SendAsync(AuthorizationTelemetryHttpFixture fixture,
        AdminResourcesRequest payload, bool malformed, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthorizationTelemetryTestProtocol.Target);
        request.Headers.Authorization = new AuthenticationHeaderValue(AuthorizationTelemetryTestProtocol.Bearer,
            AuthorizationTelemetryTestProtocol.MemberKey);
        request.Headers.Add(AuthorizationTelemetryTestProtocol.PrivateHeader, AuthorizationTelemetryTestProtocol.HeaderCanary);
        var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        var body = malformed ? json[..^AuthorizationMalformedHttpExportProtocol.TruncatedClosingCharacters] : json;
        request.Content = new StringContent(body, Encoding.UTF8, AuthorizationMalformedHttpExportProtocol.JsonMediaType);
        return await fixture.Client.SendAsync(request, cancellationToken);
    }
}
