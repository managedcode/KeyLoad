using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>AC-FTS-001/002: actual CLUSTER text generations preserve canonical ranking and authorization.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeTextClusterTests(ClusterFixture fixture)
{
    private const string Needle = "needle";
    private const string FullwidthNeedle = "ＮＥＥＤＬＥ";
    private const double FirstSingleScore = 1d / 61d;
    private const double SecondSingleScore = 1d / 62d;
    private const double FirstHybridScore = 2d / 61d;
    private const double SecondHybridScore = 2d / 62d;
    private const double ThirdHybridScore = 1d / 63d;
    private const double SecondCrossBranchScore = 1d / 61d + 1d / 62d;

    [Test]
    public async Task AcFts001ActualSdkAndOfficialMcpMatchIndependentTextAndHybridOracle()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await NativeTextClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, false, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Text(Needle),
            [(NativeTextClusterScenario.FirstId, FirstSingleScore), (NativeTextClusterScenario.SecondId, SecondSingleScore)],
            deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Text(FullwidthNeedle),
            [(NativeTextClusterScenario.FirstId, FirstSingleScore), (NativeTextClusterScenario.SecondId, SecondSingleScore)],
            deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Text("КИЇВ"),
            [(NativeTextClusterScenario.FirstId, FirstSingleScore)], deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Hybrid(),
        [
            (NativeTextClusterScenario.FirstId, FirstHybridScore),
            (NativeTextClusterScenario.SecondId, SecondHybridScore),
            (NativeTextClusterScenario.ThirdId, ThirdHybridScore)
        ], deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Hybrid(limit: 1),
            [(NativeTextClusterScenario.FirstId, FirstHybridScore)], deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Hybrid("plain", limit: 1),
            [(NativeTextClusterScenario.SecondId, SecondCrossBranchScore)], deadline.Token);
    }

    [Test]
    public async Task AcFts002PersistedRowFieldRedactionFreshnessAndRevocationMatchBothClients()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await NativeTextClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, true, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        var restricted = await NativeTextPublicOutcomeAssertions.SdkSuccessAsync(await sdk.SearchAsync(
            scenario.Text(Needle), deadline.Token));
        await Assert.That(restricted).HasSingleItem();
        await Assert.That(restricted[0].Document.Reference.Id).IsEqualTo(NativeTextClusterScenario.FirstId);
        await AssertProjectedAsync(restricted[0].Document);
        var official = await NativeTextPublicOutcomeAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text(Needle), deadline.Token));
        await Assert.That(JsonDefaults.Serialize(restricted).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(official.Value))).IsTrue();
        await VerifyMissingFieldGrantAsync(scenario, fixture, false, deadline.Token);
        await VerifyMissingFieldGrantAsync(scenario, fixture, true, deadline.Token);
        using var administratorHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        await VerifyFreshnessAsync(scenario, sdk, mcp,
            new KeyLoadClient(administratorHttp, fixture.AdminKey, IntegrationClientOptions.Execution()), deadline.Token);
        var revoked = identity.Principal with
        {
            Revoked = true,
            PolicyEpoch = identity.Principal.PolicyEpoch + 1
        };
        await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution())
            .ConfigurePrincipalAsync(Guid.NewGuid(), revoked, deadline.Token));
        await AssertSdkUnauthenticatedAsync(await sdk.SearchAsync(scenario.Text(Needle), deadline.Token));
        var unauthorized = await Assert.ThrowsAsync<HttpRequestException>(() => mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text(Needle), deadline.Token));
        await Assert.That(unauthorized!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(unauthorized.Message.Contains(identity.Secret, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task VerifyMissingFieldGrantAsync(NativeTextClusterScenario scenario, ClusterFixture fixture,
        bool textGrant, CancellationToken cancellationToken)
    {
        var identity = await scenario.CreateReaderAsync(fixture, textGrant, !textGrant, false, false, cancellationToken);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        var request = textGrant ? scenario.Hybrid() : scenario.Text(Needle);
        var denied = await sdk.SearchAsync(request, cancellationToken);
        await AssertSdkFailureAsync(denied, ErrorCode.PermissionDenied);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            identity.Secret, cancellationToken);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SearchExecute,
            request, cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task VerifyFreshnessAsync(NativeTextClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, KeyLoadClient administrator, CancellationToken cancellationToken)
    {
        var updateCommand = new CommandRequest(Guid.NewGuid(), scenario.Partition,
        [
            new PutDocument(NativeTextClusterScenario.Collection, NativeTextClusterScenario.FirstId,
                NativeTextClusterScenario.Document("fresh wording", NativeTextClusterScenario.OwnerA),
                NativeTextClusterScenario.FirstRevision, new(NativeTextClusterScenario.OwnerA)),
            new PutVector(NativeTextClusterScenario.Collection, NativeTextClusterScenario.FirstId,
                NativeTextClusterScenario.VectorField, [1, 0], NativeTextClusterScenario.SpaceFor(),
                NativeTextClusterScenario.UpdatedRevision),
            new DeleteDocument(NativeTextClusterScenario.Collection, NativeTextClusterScenario.SecondId,
                NativeTextClusterScenario.FirstRevision)
        ]);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(updateCommand, cancellationToken));
        var sdkAfterDelete = await NativeTextPublicOutcomeAssertions.SdkSuccessAsync(await sdk.SearchAsync(
            scenario.Text(Needle), cancellationToken));
        var mcpAfterDelete = await NativeTextPublicOutcomeAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text(Needle), cancellationToken));
        await Assert.That(sdkAfterDelete).IsEmpty();
        await Assert.That(mcpAfterDelete.Value).IsEmpty();
        var sdkUpdated = await NativeTextPublicOutcomeAssertions.SdkSuccessAsync(await sdk.SearchAsync(
            scenario.Text("fresh"), cancellationToken));
        var mcpUpdated = await NativeTextPublicOutcomeAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text("fresh"), cancellationToken));
        await Assert.That(sdkUpdated).HasSingleItem();
        await Assert.That(sdkUpdated[0].Document.Reference.Id).IsEqualTo(NativeTextClusterScenario.FirstId);
        await Assert.That(JsonDefaults.Serialize(sdkUpdated).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpUpdated.Value))).IsTrue();
    }

    private static async Task VerifyRanksAsync(KeyLoadClient sdk, McpOfficialClient mcp, SearchRequest request,
        (string Id, double Score)[] expected, CancellationToken cancellationToken)
    {
        var actual = await NativeTextPublicOutcomeAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, cancellationToken));
        var official = await NativeTextPublicOutcomeAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, request, cancellationToken));
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Document.Reference.Id).IsEqualTo(expected[index].Id);
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score);
            await AssertProjectedAsync(actual[index].Document);
        }
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(official.Value))).IsTrue();
    }

    private static async Task AssertProjectedAsync(DocumentResult document)
    {
        await Assert.That(document.Redacted).IsTrue();
        await Assert.That(document.RedactedFields).Contains(NativeTextClusterScenario.SecretField);
        await Assert.That(document.Json.Contains(NativeTextClusterScenario.Secret, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertSdkUnauthenticatedAsync(Result<RankedDocument[]> result)
        => await AssertSdkFailureAsync(result, ErrorCode.Unauthenticated);

    private static async Task AssertSdkFailureAsync<T>(Result<T> result, ErrorCode code)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
    }
}
