using System.Net;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class ConcurrentPrincipalPolicyContinuation
{
    private const int RestrictedAdvance = 1;
    private const int RevokedAdvance = 2;
    private const int RepairedAdvance = 3;

    internal static async Task RunAsync(ClusterFixture fixture, McpOfficialClient firstClient,
        McpOfficialClient secondClient, McpDocumentScenario first, McpDocumentScenario second,
        McpPersistedIdentity firstIdentity, McpPersistedIdentity secondIdentity, CancellationToken token)
    {
        var original = firstIdentity.Principal;
        await PersistAsync(fixture, original with
        {
            Grants = [],
            PolicyEpoch = original.PolicyEpoch + RestrictedAdvance
        }, token);
        var restricted = await ConcurrentPrincipalAuthorityAssertions.CaptureAsync(fixture, first, second, token);
        await ConcurrentAsync(DeniedAsync(firstClient, first, firstIdentity, token),
            HealthyAsync(secondClient, second, token));
        await UnchangedAsync(fixture, first, second, restricted, token);
        await PersistAsync(fixture, original with
        {
            Revoked = true,
            PolicyEpoch = original.PolicyEpoch + RevokedAdvance
        }, token);
        var revoked = await ConcurrentPrincipalAuthorityAssertions.CaptureAsync(fixture, first, second, token);
        await ConcurrentAsync(RevokedAsync(firstClient, first, firstIdentity, token),
            HealthyAsync(secondClient, second, token));
        await UnchangedAsync(fixture, first, second, revoked, token);
        await PersistAsync(fixture, original with
        {
            PolicyEpoch = original.PolicyEpoch + RepairedAdvance
        }, token);
        var repaired = await ConcurrentPrincipalAuthorityAssertions.CaptureAsync(fixture, first, second, token);
        await ConcurrentAsync(HealthyAsync(firstClient, first, token), HealthyAsync(secondClient, second, token));
        await ConcurrentAsync(DeniedAsync(firstClient, second, firstIdentity, token),
            DeniedAsync(secondClient, first, secondIdentity, token));
        await UnchangedAsync(fixture, first, second, repaired, token);
        await ConcurrentPrincipalAuthorityAssertions.FollowingAsync(firstClient, secondClient, first, second, token);
        await UnchangedAsync(fixture, first, second, repaired, token);
    }

    private static async Task ConcurrentAsync(Task first, Task second)
    {
        var firstFailures = new List<Exception>();
        var secondFailures = new List<Exception>();
        await Task.WhenAll(
            global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => first, firstFailures),
            global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => second, secondFailures));
        firstFailures.AddRange(secondFailures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(firstFailures);
    }

    private static Task<PrincipalRecord> PersistAsync(ClusterFixture fixture, PrincipalRecord expected,
        CancellationToken token)
        => AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1, async root =>
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(
                await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
            await SqlClusterProtocol.EqualAsync(expected, actual);
            return actual;
        });

    private static async Task HealthyAsync(McpOfficialClient client, McpDocumentScenario scenario,
        CancellationToken token)
    {
        var actual = await PublicReadAssertions.RequiredDocumentAsync(await client.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), token));
        await ConcurrentPrincipalAuthorityAssertions.LiteralAsync(scenario, actual.Value);
    }

    private static async Task DeniedAsync(McpOfficialClient client, McpDocumentScenario scenario,
        McpPersistedIdentity identity, CancellationToken token)
    {
        var actual = await client.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference), token);
        await McpCallerAssertions.ErrorAsync(actual, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(actual, identity.Secret, McpDocumentProtocol.PrivateValue);
    }

    private static async Task RevokedAsync(McpOfficialClient client, McpDocumentScenario scenario,
        McpPersistedIdentity identity, CancellationToken token)
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), token))
            ?? throw new InvalidOperationException("The revoked official client did not retain its failure.");
        await Assert.That(error.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(error.Message.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task UnchangedAsync(ClusterFixture fixture, McpDocumentScenario first,
        McpDocumentScenario second, global::System.Text.Json.JsonElement expected, CancellationToken token)
    {
        var actual = await ConcurrentPrincipalAuthorityAssertions.CaptureAsync(fixture, first, second, token);
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }
}
