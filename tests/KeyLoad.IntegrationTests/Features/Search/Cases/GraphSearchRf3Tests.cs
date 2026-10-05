using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>AC-GSEARCH-006: direct graph operators execute through real RF3 SDK and official MCP callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphSearchRf3Tests(ClusterFixture fixture)
{
    private const double RankOneScore = 1d / (GraphSearchRf3Scenario.FusionConstant + 1);
    private const double RankTwoScore = 1d / (GraphSearchRf3Scenario.FusionConstant + 2);
    private const double RankThreeScore = 1d / (GraphSearchRf3Scenario.FusionConstant + 3);
    private const double RankFourScore = 1d / (GraphSearchRf3Scenario.FusionConstant + 4);

    [Test]
    public async Task AcGsearch006SdkAndOfficialMcpReturnIndependentMultiseedRanksAndContext()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await GraphSearchRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, deadline.Token);
        using var sdkHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(sdkHttp, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var request = GraphSearchRf3Scenario.Request(scenario.Partition, limit: 2) with
        { Expansion = new(GraphSearchRf3Scenario.Graph, MaxDepth: 1, MaxVertices: 10, MaxEdges: 20) };

        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(request, deadline.Token));
        var mcpResult = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            GraphSearchRf3Scenario.SearchGraphTool, request, deadline.Token));
        await GraphSearchRf3Assertions.AssertHitsAsync(sdkResult,
            (GraphSearchRf3Scenario.Alpha, RankOneScore), (GraphSearchRf3Scenario.Beta, RankTwoScore));
        await GraphSearchRf3Assertions.AssertExpansionAsync(sdkResult, GraphSearchRf3Scenario.Context);
        await GraphSearchRf3Assertions.AssertEquivalentAsync(sdkResult, mcpResult.Value);
    }

    [Test]
    public async Task AcGsearch006PersistedLabelGrantControlsEmptyLabelsAndWritesRefreshReachability()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await GraphSearchRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, deadline.Token);
        using var sdkHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(sdkHttp, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        var baseRequest = GraphSearchRf3Scenario.Request(scenario.Partition);
        var labeled = baseRequest with
        { Retriever = new(baseRequest.Retriever!.Walk with { Labels = [GraphSearchRf3Scenario.Label] }) };

        await AssertLabelDeniedAsync(sdk, mcp, labeled, deadline.Token);
        identity = await GraphSearchRf3Scenario.GrantLabelUseAsync(fixture, identity, deadline.Token);
        var emptyLabels = labeled with
        { Retriever = new(labeled.Retriever!.Walk with { Labels = ImmutableArray<string>.Empty }) };
        var emptyAllowed = GraphSearchRf3Scenario.Request(scenario.Partition, allowedIds: ImmutableArray<string>.Empty);
        await AssertEmptyAsync(sdk, mcp, emptyLabels, deadline.Token);
        await AssertEmptyAsync(sdk, mcp, emptyAllowed, deadline.Token);

        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey);
        await scenario.AddReachableDocumentAsync(admin, deadline.Token);
        var updated = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(labeled, deadline.Token));
        await GraphSearchRf3Assertions.AssertHitsAsync(updated,
            (GraphSearchRf3Scenario.Alpha, RankOneScore), (GraphSearchRf3Scenario.Beta, RankTwoScore),
            ("delta", RankThreeScore), (GraphSearchRf3Scenario.Gamma, RankFourScore));
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            GraphSearchRf3Scenario.SearchGraphTool, labeled, deadline.Token));
        await GraphSearchRf3Assertions.AssertEquivalentAsync(updated, official.Value);
        await RevokeAndAssertAsync(admin, sdk, mcp, identity, labeled, deadline.Token);
    }

    private static async Task AssertLabelDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        GraphSearchRequest request, CancellationToken cancellationToken)
    {
        var denied = await sdk.GraphSearchAsync(request, cancellationToken);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(GraphSearchRf3Scenario.SearchGraphTool,
            request, cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task AssertEmptyAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        GraphSearchRequest request, CancellationToken cancellationToken)
    {
        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(request, cancellationToken));
        var mcpResult = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            GraphSearchRf3Scenario.SearchGraphTool, request, cancellationToken));
        await Assert.That(sdkResult.Hits).IsEmpty();
        await GraphSearchRf3Assertions.AssertEquivalentAsync(sdkResult, mcpResult.Value);
    }

    private static async Task RevokeAndAssertAsync(KeyLoadClient admin, KeyLoadClient sdk,
        McpOfficialClient mcp, McpPersistedIdentity identity, GraphSearchRequest request,
        CancellationToken cancellationToken)
    {
        var revoked = identity.Principal with
        { Revoked = true, PolicyEpoch = identity.Principal.PolicyEpoch + 1 };
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), revoked,
            cancellationToken));
        var sdkResult = await sdk.GraphSearchAsync(request, cancellationToken);
        await Assert.That(sdkResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => mcp.CallAsync(
            GraphSearchRf3Scenario.SearchGraphTool, request, cancellationToken));
        await Assert.That(failure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
    }
}
