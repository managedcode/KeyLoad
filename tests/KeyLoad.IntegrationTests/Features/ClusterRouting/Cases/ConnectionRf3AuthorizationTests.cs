using System.Net;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ConnectionRf3AuthorizationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task AcCrs059FreshPersistedAuthorizationDoesNotLeakAcrossConnectionOperations(bool useMcp)
        => ConnectionRf3Scenario.RunAsync((scenario, token) => ExecuteAsync(scenario, useMcp, token),
            TestContext.Current!.Execution.CancellationToken);

    private static async Task ExecuteAsync(ConnectionRf3Scenario scenario, bool useMcp, CancellationToken token)
    {
        var caller = await scenario.CallerAsync(token);
        var first = await scenario.ObserveAsync(scenario.Identity, Guid.Empty, GrainReadKind.Document,
            () => ConnectionRf3PublicOperations.ReadAsync(caller, scenario.Identity, useMcp, token), token);
        await ConnectionRf3PublicOperations.DocumentAsync(first.Result, scenario.Identity, first.Witness,
            RequestCqrsRf3Protocol.DocumentJson, ConnectionRf3Protocol.InitialRevision);
        var principal = new PrincipalRecord(scenario.Identity.PrincipalId, scenario.Identity.Partition.TenantId,
            [new(scenario.Identity.Partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        { Revoked = true, PolicyEpoch = ConnectionRf3Protocol.RevokedPolicyEpoch };
        await McpCallerAssertions.SdkSuccessAsync(await scenario.Administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), principal, token));
        await VerifyRejectedAsync(caller, scenario.Identity, useMcp, token);
        var independent = caller.WithCredential(scenario.SecondIdentity.Secret);
        var reference = ConnectionRf3PublicOperations.Reference(scenario.SecondIdentity);
        var following = await scenario.ObserveAsync(scenario.SecondIdentity, Guid.Empty, GrainReadKind.Document,
            () => independent.GetAsync(reference, token), token);
        var value = await McpCallerAssertions.SdkSuccessAsync(following.Result);
        await ConnectionRf3PublicOperations.DocumentAsync(new(value, null), scenario.SecondIdentity, following.Witness,
            RequestCqrsRf3Protocol.DocumentJson, ConnectionRf3Protocol.InitialRevision);
        await ConnectionRf3WitnessReader.SameOwnerAsync(first.Witness, following.Witness);
        await scenario.VerifyDocumentAsync(scenario.Identity, RequestCqrsRf3Protocol.DocumentJson,
            ConnectionRf3Protocol.InitialRevision, token);
        await scenario.CloseAsync(caller, first.Marker, token);
    }

    private static async Task VerifyRejectedAsync(ConnectionRf3Caller caller,
        RequestCqrsPhaseFaultIdentity identity, bool useMcp, CancellationToken token)
    {
        var reference = ConnectionRf3PublicOperations.Reference(identity);
        if (useMcp)
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => caller.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(failure.Message.Contains(identity.Secret, StringComparison.Ordinal)).IsFalse();
        }
        var rejected = await caller.Sdk.GetAsync(reference, token);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
    }
}
