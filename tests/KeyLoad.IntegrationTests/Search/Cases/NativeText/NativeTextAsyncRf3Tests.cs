using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeTextAsyncRf3Tests(ClusterFixture fixture)
{
    private const double FirstScore = 1d / 61d;
    private const double SecondScore = 1d / 62d;
    private const string FirstNode = McpCallerProtocol.Node1;
    private const string SecondNode = McpCallerProtocol.Node2;
    private const string ThirdNode = McpCallerProtocol.Node3;

    [Test]
    public async Task AcFts007AwaitedNativeSearchPreservesRf3ResultsPolicyAndHealth()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await NativeTextRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, false, deadline.Token);
        using var callerHttp = McpCallerHttp.Create(fixture, FirstNode);
        var caller = new KeyLoadClient(callerHttp, identity.Secret, IntegrationClientOptions.Execution());
        using var nodeOneHttp = McpCallerHttp.Create(fixture, FirstNode);
        using var nodeTwoHttp = McpCallerHttp.Create(fixture, SecondNode);
        using var nodeThreeHttp = McpCallerHttp.Create(fixture, ThirdNode);
        var administrators = new[]
        {
            new KeyLoadClient(nodeOneHttp, fixture.AdminKey, IntegrationClientOptions.Execution()),
            new KeyLoadClient(nodeTwoHttp, fixture.AdminKey, IntegrationClientOptions.Execution()),
            new KeyLoadClient(nodeThreeHttp, fixture.AdminKey, IntegrationClientOptions.Execution())
        };
        await using var callerMcp = await McpOfficialClient.ConnectAsync(fixture, SecondNode,
            identity.Secret, deadline.Token);
        await using var administratorMcp = await McpOfficialClient.ConnectAsync(fixture, FirstNode,
            fixture.AdminKey, deadline.Token);

        await NativeTextAsyncRf3Assertions.AssertClusterHealthyAsync(fixture, administrators,
            administratorMcp, deadline.Token);
        await VerifyInitialAndRepeatedSearchAsync(scenario, caller, callerMcp, deadline.Token);
        await UpdateCorpusAsync(scenario, administrators[0], deadline.Token);
        await VerifyFreshReplacementAsync(scenario, caller, callerMcp, deadline.Token);
        var grantUpdated = await RemoveTextGrantAsync(identity.Principal, administrators[0], deadline.Token);
        await VerifyMissingGrantAsync(scenario, caller, callerMcp, deadline.Token);
        var revoked = await RevokeAsync(grantUpdated, administrators[0], deadline.Token);
        await Assert.That(revoked.PolicyEpoch).IsGreaterThan(grantUpdated.PolicyEpoch);
        await VerifyRevokedCredentialAsync(scenario, caller, callerMcp, identity.Secret, revoked.PolicyEpoch,
            deadline.Token);
        await NativeTextAsyncRf3Assertions.AssertSearchAsync(administrators[0], administratorMcp,
            scenario.Text("fresh"), [(NativeTextRf3Scenario.FirstId, FirstScore)], verifyRedaction: false,
            deadline.Token);
        await NativeTextAsyncRf3Assertions.AssertClusterHealthyAsync(fixture, administrators,
            administratorMcp, deadline.Token);
    }

    private static async Task VerifyInitialAndRepeatedSearchAsync(NativeTextRf3Scenario scenario,
        KeyLoadClient caller, McpOfficialClient callerMcp, CancellationToken cancellationToken)
    {
        var request = scenario.Text("needle");
        var expected = new[]
        {
            (NativeTextRf3Scenario.FirstId, FirstScore),
            (NativeTextRf3Scenario.SecondId, SecondScore)
        };
        await NativeTextAsyncRf3Assertions.AssertSearchAsync(caller, callerMcp, request, expected,
            verifyRedaction: true, cancellationToken);
        await NativeTextAsyncRf3Assertions.AssertSearchAsync(caller, callerMcp, request, expected,
            verifyRedaction: true, cancellationToken);
    }

    private static async Task UpdateCorpusAsync(NativeTextRf3Scenario scenario, KeyLoadClient administrator,
        CancellationToken cancellationToken)
    {
        var update = new CommandRequest(Guid.NewGuid(), scenario.Partition,
        [
            new PutDocument(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.FirstId,
                NativeTextRf3Scenario.Document("fresh wording", NativeTextRf3Scenario.OwnerA),
                NativeTextRf3Scenario.FirstRevision, new(NativeTextRf3Scenario.OwnerA)),
            new PutVector(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.FirstId,
                NativeTextRf3Scenario.VectorField, [1, 0], NativeTextRf3Scenario.SpaceFor(),
                NativeTextRf3Scenario.UpdatedRevision),
            new DeleteDocument(NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.SecondId,
                NativeTextRf3Scenario.FirstRevision)
        ]);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(update, cancellationToken));
    }

    private static async Task VerifyFreshReplacementAsync(NativeTextRf3Scenario scenario,
        KeyLoadClient caller, McpOfficialClient callerMcp, CancellationToken cancellationToken)
    {
        await NativeTextAsyncRf3Assertions.AssertSearchAsync(caller, callerMcp, scenario.Text("needle"), [],
            verifyRedaction: true, cancellationToken);
        await NativeTextAsyncRf3Assertions.AssertSearchAsync(caller, callerMcp, scenario.Text("fresh"),
            [(NativeTextRf3Scenario.FirstId, FirstScore)], verifyRedaction: true, cancellationToken);
    }

    private static async Task<PrincipalRecord> RemoveTextGrantAsync(PrincipalRecord principal,
        KeyLoadClient administrator, CancellationToken cancellationToken)
    {
        var update = principal with
        {
            FieldGrants = principal.FieldGrants.Remove(NativeTextRf3Scenario.TextUseGrant),
            PolicyEpoch = checked(principal.PolicyEpoch + 1)
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), update, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(update.PolicyEpoch);
        await Assert.That(persisted.FieldGrants.Contains(NativeTextRf3Scenario.TextUseGrant,
            StringComparer.Ordinal)).IsFalse();
        return persisted;
    }

    private static async Task VerifyMissingGrantAsync(NativeTextRf3Scenario scenario, KeyLoadClient caller,
        McpOfficialClient callerMcp, CancellationToken cancellationToken)
    {
        var denied = await caller.SearchAsync(scenario.Text("fresh"), cancellationToken);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await callerMcp.CallAsync(McpCallerTools.SearchExecute,
            scenario.Text("fresh"), cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task<PrincipalRecord> RevokeAsync(PrincipalRecord persistedPrincipal,
        KeyLoadClient administrator, CancellationToken cancellationToken)
    {
        var update = persistedPrincipal with
        {
            Revoked = true,
            PolicyEpoch = checked(persistedPrincipal.PolicyEpoch + 1)
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), update, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(update.PolicyEpoch);
        await Assert.That(persisted.Revoked).IsTrue();
        return persisted;
    }

    private static async Task VerifyRevokedCredentialAsync(NativeTextRf3Scenario scenario, KeyLoadClient caller,
        McpOfficialClient callerMcp, string secret, long policyEpoch, CancellationToken cancellationToken)
    {
        await Assert.That(policyEpoch).IsGreaterThan(0L);
        var denied = await caller.SearchAsync(scenario.Text("fresh"), cancellationToken);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.Unauthenticated.ToString());
        var unauthorized = await Assert.ThrowsAsync<HttpRequestException>(() => callerMcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text("fresh"), cancellationToken));
        await Assert.That(unauthorized!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(unauthorized.Message.Contains(secret, StringComparison.Ordinal)).IsFalse();
    }
}
