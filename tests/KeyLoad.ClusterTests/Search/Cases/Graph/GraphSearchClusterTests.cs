using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Search;

/// <summary>AC-GSEARCH-006: direct graph operators execute through real Orleans SDK and official MCP callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphSearchClusterTests(ClusterFixture fixture)
{
    private const double RankOneScore = 1d / (GraphSearchClusterScenario.FusionConstant + 1);
    private const double RankTwoScore = 1d / (GraphSearchClusterScenario.FusionConstant + 2);
    private const double RankThreeScore = 1d / (GraphSearchClusterScenario.FusionConstant + 3);
    private const double RankFourScore = 1d / (GraphSearchClusterScenario.FusionConstant + 4);

    [Test]
    public async Task AcGsearch006SdkAndOfficialMcpReturnIndependentMultiseedRanksAndContext()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await GraphSearchClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, deadline.Token);
        using var sdkHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(sdkHttp, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var request = GraphSearchClusterScenario.Request(scenario.Partition, limit: 2) with
        {
            Expansion = new(GraphSearchClusterScenario.Graph, MaxDepth: 1, MaxVertices: 10, MaxEdges: 20)
        };

        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(request, deadline.Token));
        var mcpResult = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            GraphSearchClusterScenario.SearchGraphTool, request, deadline.Token));
        await GraphSearchClusterAssertions.AssertHitsAsync(sdkResult,
            (GraphSearchClusterScenario.Alpha, RankOneScore), (GraphSearchClusterScenario.Beta, RankTwoScore));
        await GraphSearchClusterAssertions.AssertExpansionAsync(sdkResult, GraphSearchClusterScenario.Context);
        await GraphSearchClusterAssertions.AssertEquivalentAsync(sdkResult, mcpResult.Value);
    }

    [Test]
    public async Task AcGsearch006PersistedLabelGrantControlsEmptyLabelsAndWritesRefreshReachability()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await GraphSearchClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, deadline.Token);
        using var sdkHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(sdkHttp, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        var baseRequest = GraphSearchClusterScenario.Request(scenario.Partition);
        var labeled = baseRequest with
        {
            Retriever = new(baseRequest.Retriever!.Walk with
            {
                Labels = [GraphSearchClusterScenario.Label]
            })
        };

        await AssertLabelDeniedAsync(sdk, mcp, labeled, deadline.Token);
        identity = await GraphSearchClusterScenario.GrantLabelUseAsync(fixture, identity, deadline.Token);
        var emptyLabels = labeled with
        {
            Retriever = new(labeled.Retriever!.Walk with
            {
                Labels = ImmutableArray<string>.Empty
            })
        };
        var emptyAllowed = GraphSearchClusterScenario.Request(scenario.Partition, allowedIds: ImmutableArray<string>.Empty);
        await AssertEmptyAsync(sdk, mcp, emptyLabels, deadline.Token);
        await AssertEmptyAsync(sdk, mcp, emptyAllowed, deadline.Token);

        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await scenario.AddReachableDocumentAsync(admin, deadline.Token);
        var updated = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(labeled, deadline.Token));
        await GraphSearchClusterAssertions.AssertHitsAsync(updated,
            (GraphSearchClusterScenario.Alpha, RankOneScore), (GraphSearchClusterScenario.Beta, RankTwoScore),
            ("delta", RankThreeScore), (GraphSearchClusterScenario.Gamma, RankFourScore));
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            GraphSearchClusterScenario.SearchGraphTool, labeled, deadline.Token));
        await GraphSearchClusterAssertions.AssertEquivalentAsync(updated, official.Value);
        await RevokeAndAssertAsync(admin, sdk, mcp, identity, labeled, deadline.Token);
    }

    private static async Task AssertLabelDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        GraphSearchRequest request, CancellationToken cancellationToken)
    {
        var denied = await sdk.GraphSearchAsync(request, cancellationToken);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(GraphSearchClusterScenario.SearchGraphTool,
            request, cancellationToken), ErrorCode.PermissionDenied, dispatched: true)).Value;
    }

    private static async Task AssertEmptyAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        GraphSearchRequest request, CancellationToken cancellationToken)
    {
        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(request, cancellationToken));
        var mcpResult = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            GraphSearchClusterScenario.SearchGraphTool, request, cancellationToken));
        await Assert.That(sdkResult.Hits).IsEmpty();
        await GraphSearchClusterAssertions.AssertEquivalentAsync(sdkResult, mcpResult.Value);
    }

    private static async Task RevokeAndAssertAsync(KeyLoadClient admin, KeyLoadClient sdk,
        McpOfficialClient mcp, McpPersistedIdentity identity, GraphSearchRequest request,
        CancellationToken cancellationToken)
    {
        var revoked = identity.Principal with
        {
            Revoked = true,
            PolicyEpoch = identity.Principal.PolicyEpoch + 1
        };
        await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), revoked,
            cancellationToken));
        var sdkResult = await sdk.GraphSearchAsync(request, cancellationToken);
        await Assert.That(sdkResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => mcp.CallAsync(
            GraphSearchClusterScenario.SearchGraphTool, request, cancellationToken));
        await Assert.That(failure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
    }
}
