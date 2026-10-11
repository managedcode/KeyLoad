using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationCredentialHttpExportFlow
{
    internal static Task ExecuteAsync(AuthorizationTelemetryHttpFixture fixture) => RunAsync(fixture, expired: false);

    internal static Task ExecuteExpiredAsync(AuthorizationTelemetryHttpFixture fixture) => RunAsync(fixture, expired: true);

    private static async Task RunAsync(AuthorizationTelemetryHttpFixture fixture, bool expired)
    {
        await AuthorizationTelemetryPolicyRepair.RepairAsync(fixture.Database);
        var originalKey = fixture.Database.Store.Read(view => global::KeyLoad.Storage.StorageRecords.GetRecord<global::KeyLoad.ApiKeyRecord>(
            view, global::KeyLoad.Core.KeySpace.ApiKey(AuthorizationTelemetryTestProtocol.Member)))
            ?? throw new InvalidOperationException(AuthorizationCredentialHttpExportProtocol.MissingOriginalCredential);
        var refusedKey = expired
            ? originalKey with { ExpiresAt = TimeProvider.System.GetUtcNow() - AuthorizationCredentialHttpExportProtocol.ExpiredOffset }
            : originalKey with { Revoked = true };
        _ = await AuthorizationCredentialHttpExportAssertions.ConfigureAsync(fixture.Database, refusedKey);
        var revoked = AuthorizationTelemetryPolicyRepair.Capture(fixture.Database);
        var partition = fixture.Database.Partition;
        var payload = new AdminResourcesRequest(partition.TenantId, partition.DatabaseId,
            AuthorizationTelemetryTestProtocol.BodyCanary);
        using var deadline = fixture.Deadline();
        using (var caller = new AuthorizationTelemetryCallerActivities(AuthorizationUnsafeSpanState.None))
        {
            await AuthorizationTelemetryNativeAssertions.CallerAsync(caller);
            using var denied = await SendAsync(fixture, payload, deadline.Token);
            await AuthorizationCredentialHttpExportAssertions.ProblemAsync(denied, deadline.Token);
        }
        await AuthorizationTelemetryPolicyRepair.UnchangedAsync(fixture.Database, revoked);
        var repair = await AuthorizationCredentialHttpExportAssertions.ConfigureAsync(fixture.Database, originalKey);
        var repaired = AuthorizationTelemetryPolicyRepair.Capture(fixture.Database);
        var position = fixture.Database.Database.LastApplied;
        using (var caller = new AuthorizationTelemetryCallerActivities(AuthorizationUnsafeSpanState.None))
        {
            await AuthorizationTelemetryNativeAssertions.CallerAsync(caller);
            using var healthy = await SendAsync(fixture, payload with { AfterName = null }, deadline.Token);
            await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var catalog = await healthy.Content.ReadFromJsonAsync<AdminResourcesPage>(JsonDefaults.Options, deadline.Token);
            await AuthorizationTelemetryAssertions.CatalogAsync(catalog!, partition, position);
        }
        await AuthorizationTelemetryPolicyRepair.UnchangedAsync(fixture.Database, repaired);
        await fixture.JoinProducersAsync(deadline.Token);
        await fixture.AwaitExportsAsync(AuthorizationUnsafeSpanState.None, deadline.Token);
        await AuthorizationTelemetryAssertions.ExportsAsync(fixture, AuthorizationUnsafeSpanState.None,
            suppressedTrace: null, authenticationFailure: true);
        await AuthorizationCredentialColdAssertions.RunAsync(fixture, repair, deadline.Token);
    }

    private static async Task<HttpResponseMessage> SendAsync(AuthorizationTelemetryHttpFixture fixture,
        AdminResourcesRequest payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthorizationTelemetryTestProtocol.Target);
        request.Headers.Authorization = new AuthenticationHeaderValue(AuthorizationTelemetryTestProtocol.Bearer,
            AuthorizationTelemetryTestProtocol.MemberKey);
        request.Headers.Add(AuthorizationTelemetryTestProtocol.PrivateHeader, AuthorizationTelemetryTestProtocol.HeaderCanary);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonDefaults.Options),
            Encoding.UTF8, AuthorizationMalformedHttpExportProtocol.JsonMediaType);
        return await fixture.Client.SendAsync(request, cancellationToken);
    }
}
