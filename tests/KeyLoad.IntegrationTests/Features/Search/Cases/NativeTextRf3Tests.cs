using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>AC-FTS-001/002: actual RF3 text generations preserve canonical ranking and authorization.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeTextRf3Tests(ClusterFixture fixture)
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
        var scenario = await NativeTextRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, false, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Text(Needle),
            [(NativeTextRf3Scenario.FirstId, FirstSingleScore), (NativeTextRf3Scenario.SecondId, SecondSingleScore)],
            deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Text(FullwidthNeedle),
            [(NativeTextRf3Scenario.FirstId, FirstSingleScore), (NativeTextRf3Scenario.SecondId, SecondSingleScore)],
            deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Text("КИЇВ"),
            [(NativeTextRf3Scenario.FirstId, FirstSingleScore)], deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Hybrid(),
        [
            (NativeTextRf3Scenario.FirstId, FirstHybridScore),
            (NativeTextRf3Scenario.SecondId, SecondHybridScore),
            (NativeTextRf3Scenario.ThirdId, ThirdHybridScore)
        ], deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Hybrid(limit: 1),
            [(NativeTextRf3Scenario.FirstId, FirstHybridScore)], deadline.Token);
        await VerifyRanksAsync(sdk, mcp, scenario.Hybrid("plain", limit: 1),
            [(NativeTextRf3Scenario.SecondId, SecondCrossBranchScore)], deadline.Token);
    }

    [Test]
    public async Task AcFts002PersistedRowFieldRedactionFreshnessAndRevocationMatchBothClients()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await NativeTextRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, true, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        var restricted = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(
            scenario.Text(Needle), deadline.Token));
        await Assert.That(restricted).HasSingleItem();
        await Assert.That(restricted[0].Document.Reference.Id).IsEqualTo(NativeTextRf3Scenario.FirstId);
        await AssertProjectedAsync(restricted[0].Document);
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text(Needle), deadline.Token));
        await Assert.That(JsonDefaults.Serialize(restricted).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(official.Value))).IsTrue();
        await VerifyMissingFieldGrantAsync(scenario, fixture, false, deadline.Token);
        await VerifyMissingFieldGrantAsync(scenario, fixture, true, deadline.Token);
        using var administratorHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        await VerifyFreshnessAsync(scenario, sdk, mcp,
            new KeyLoadClient(administratorHttp, fixture.AdminKey), deadline.Token);
        var revoked = identity.Principal with { Revoked = true, PolicyEpoch = identity.Principal.PolicyEpoch + 1 };
        await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(http, fixture.AdminKey)
            .ConfigurePrincipalAsync(Guid.NewGuid(), revoked, deadline.Token));
        await AssertSdkUnauthenticatedAsync(await sdk.SearchAsync(scenario.Text(Needle), deadline.Token));
        var unauthorized = await Assert.ThrowsAsync<HttpRequestException>(() => mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text(Needle), deadline.Token));
        await Assert.That(unauthorized!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(unauthorized.Message.Contains(identity.Secret, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task VerifyMissingFieldGrantAsync(NativeTextRf3Scenario scenario, ClusterFixture fixture,
        bool textGrant, CancellationToken cancellationToken)
    {
        var identity = await scenario.CreateReaderAsync(fixture, textGrant, !textGrant, false, false, cancellationToken);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var sdk = new KeyLoadClient(http, identity.Secret);
        var request = textGrant ? scenario.Hybrid() : scenario.Text(Needle);
        var denied = await sdk.SearchAsync(request, cancellationToken);
        await AssertSdkFailureAsync(denied, ErrorCode.PermissionDenied);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            identity.Secret, cancellationToken);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SearchExecute,
            request, cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task VerifyFreshnessAsync(NativeTextRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, KeyLoadClient administrator, CancellationToken cancellationToken)
    {
        var updateCommand = new CommandRequest(Guid.NewGuid(), scenario.Partition,
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
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(updateCommand, cancellationToken));
        var sdkAfterDelete = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(
            scenario.Text(Needle), cancellationToken));
        var mcpAfterDelete = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text(Needle), cancellationToken));
        await Assert.That(sdkAfterDelete).IsEmpty();
        await Assert.That(mcpAfterDelete.Value).IsEmpty();
        var sdkUpdated = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(
            scenario.Text("fresh"), cancellationToken));
        var mcpUpdated = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Text("fresh"), cancellationToken));
        await Assert.That(sdkUpdated).HasSingleItem();
        await Assert.That(sdkUpdated[0].Document.Reference.Id).IsEqualTo(NativeTextRf3Scenario.FirstId);
        await Assert.That(JsonDefaults.Serialize(sdkUpdated).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpUpdated.Value))).IsTrue();
    }

    private static async Task VerifyRanksAsync(KeyLoadClient sdk, McpOfficialClient mcp, SearchRequest request,
        (string Id, double Score)[] expected, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, cancellationToken));
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
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
        await Assert.That(document.RedactedFields).Contains(NativeTextRf3Scenario.SecretField);
        await Assert.That(document.Json.Contains(NativeTextRf3Scenario.Secret, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertSdkUnauthenticatedAsync(Result<RankedDocument[]> result)
        => await AssertSdkFailureAsync(result, ErrorCode.Unauthenticated);

    private static async Task AssertSdkFailureAsync<T>(Result<T> result, ErrorCode code)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
    }
}
