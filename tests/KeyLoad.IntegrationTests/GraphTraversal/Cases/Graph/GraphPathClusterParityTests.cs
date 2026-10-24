using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.GraphTraversal;

/// <summary>AC-GRAPH-009: actual SDK and official MCP expose the same bounded shortest-path result.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphPathClusterParityTests(ClusterFixture fixture)
{
    private const string ForeignPartitionKey = "key";
    [Test]
    public async Task AcGraph009DirectAndSqlSdkMcpReturnExactTiedPathAndProjectedEdges()
    {
        using var deadline = McpCallerDeadline.Create();
        {
            var seed = await GraphPathClusterScenario.CreateAsync(fixture, deadline.Token);
            var identity = await GraphPathClusterScenario.GrantLabelUseAsync(fixture, seed, deadline.Token);
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                identity.Secret, deadline.Token);
            var direct = GraphPathClusterScenario.Request(seed, labels: ImmutableArray.Create(GraphPathClusterScenario.Label));
            var sql = GraphPathClusterScenario.SqlRequest(seed, label: GraphPathClusterScenario.Label);

            var sdkDirect = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(direct, deadline.Token));
            var mcpDirect = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
                GraphPathClusterScenario.DirectTool, direct, deadline.Token));
            var sdkSql = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(sql, deadline.Token));
            var mcpSql = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
                GraphPathClusterScenario.SqlTool, sql, deadline.Token));
            var vertices = seed.StoredVertices([seed.Source, seed.FirstBranch, seed.Target]);
            var edges = new[] { "a-branch-a", "a-end-a" };

            await GraphPathClusterAssertions.AssertPathAsync(sdkDirect, vertices, edges, GraphPathClusterScenario.Label);
            await GraphPathClusterAssertions.AssertProjectedAsync(sdkDirect);
            await GraphPathClusterAssertions.AssertEquivalentAsync(sdkDirect, mcpDirect.Value);
            await GraphPathClusterAssertions.AssertEquivalentAsync(sdkDirect, sdkSql);
            await GraphPathClusterAssertions.AssertEquivalentAsync(sdkDirect, mcpSql.Value);
        }
        await GraphPathRawReadR43Trial.RunAsync(fixture, deadline.Token);
    }

    [Test]
    public async Task AcGraph009UnrestrictedDirectPathAndZeroHopMatchOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathClusterScenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Reader.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            seed.Reader.Secret, deadline.Token);
        var direct = GraphPathClusterScenario.Request(seed);
        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(direct, deadline.Token));
        var mcpResult = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, direct, deadline.Token));
        await GraphPathClusterAssertions.AssertPathAsync(sdkResult, seed.StoredVertices([seed.Source, seed.Target]),
            ["z-shortcut"], "shortcut");
        await GraphPathClusterAssertions.AssertEquivalentAsync(sdkResult, mcpResult.Value);
        var sql = GraphPathClusterScenario.SqlRequest(seed);
        var sqlSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(sql, deadline.Token));
        var sqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.SqlTool, sql, deadline.Token));
        await GraphPathClusterAssertions.AssertEquivalentAsync(sdkResult, sqlSdk);
        await GraphPathClusterAssertions.AssertEquivalentAsync(sqlSdk, sqlMcp.Value);

        var zero = GraphPathClusterScenario.Request(seed, seed.Source, seed.Source, maxDepth: 0);
        var zeroSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(zero, deadline.Token));
        var zeroMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, zero, deadline.Token));
        await Assert.That(zeroSdk.Found).IsTrue();
        await Assert.That(zeroSdk.Hops).IsEqualTo(0);
        await Assert.That(zeroSdk.Vertices.SequenceEqual([seed.Stored(seed.Source)])).IsTrue();
        await Assert.That(zeroSdk.Edges).IsEmpty();
        await GraphPathClusterAssertions.AssertEquivalentAsync(zeroSdk, zeroMcp.Value);
        var zeroSql = GraphPathClusterScenario.SqlRequest(seed, seed.Source, seed.Source, maxDepth: 0);
        var zeroSqlSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(zeroSql, deadline.Token));
        var zeroSqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.SqlTool, zeroSql, deadline.Token));
        await GraphPathClusterAssertions.AssertEquivalentAsync(zeroSdk, zeroSqlSdk);
        await GraphPathClusterAssertions.AssertEquivalentAsync(zeroSqlSdk, zeroSqlMcp.Value);
    }

    [Test]
    public async Task AcGraph009ForeignPartitionAndInvalidRequestFailClosedAcrossSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathClusterScenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, seed.Reader.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            seed.Reader.Secret, deadline.Token);
        var request = GraphPathClusterScenario.Request(seed);
        await AssertRequestErrorAsync(sdk, mcp, request with
        {
            Version = 2
        }, ErrorCode.Validation, deadline.Token);
        await AssertSqlRequestErrorAsync(sdk, mcp, GraphPathClusterScenario.SqlRequest(seed) with
        {
            Version = 2
        },
            ErrorCode.Validation, deadline.Token);
        var foreignPartition = new PartitionRef("foreign", "db", "domain", ForeignPartitionKey);
        var foreign = request with
        {
            To = request.To with
            {
                Partition = foreignPartition
            }
        };
        await AssertRequestErrorAsync(sdk, mcp, foreign, ErrorCode.UnsupportedCapability, deadline.Token);
        // SQL scopes the whole statement with Query.Partition; endpoint literals have no partition argument.
        var foreignSqlScope = GraphPathClusterScenario.SqlRequestInPartition(seed, foreignPartition);
        await AssertSqlRequestErrorAsync(sdk, mcp, foreignSqlScope,
            ErrorCode.PermissionDenied, deadline.Token);
        await AssertRequestErrorAsync(sdk, mcp, request with
        {
            MaxDepth = 17
        }, ErrorCode.BudgetExceeded, deadline.Token);
        await AssertRequestErrorAsync(sdk, mcp, request with
        {
            MaxVertices = 10_001
        },
            ErrorCode.BudgetExceeded, deadline.Token);
        await AssertRequestErrorAsync(sdk, mcp, request with
        {
            MaxEdges = 50_001
        },
            ErrorCode.BudgetExceeded, deadline.Token);
        await AssertSqlRequestErrorAsync(sdk, mcp,
            GraphPathClusterScenario.SqlRequest(seed, maxDepth: 17), ErrorCode.BudgetExceeded, deadline.Token);
        _ = await GraphPathClusterScenario.GrantLabelUseAsync(fixture, seed, deadline.Token);
        await VerifyNoPathAndDepthAsync(seed, sdk, mcp, deadline.Token);
    }

    private static async Task VerifyNoPathAndDepthAsync(GraphPathClusterSeed seed, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken cancellationToken)
    {
        var noPathRequest = GraphPathClusterScenario.Request(seed, to: seed.Isolated);
        var noPath = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(noPathRequest, cancellationToken));
        var noPathMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, noPathRequest, cancellationToken));
        var noPathSql = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(
            GraphPathClusterScenario.SqlRequest(seed, to: seed.Isolated), cancellationToken));
        var noPathSqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.SqlTool, GraphPathClusterScenario.SqlRequest(seed, to: seed.Isolated), cancellationToken));
        await GraphPathClusterAssertions.AssertNoPathAsync(noPath);
        await GraphPathClusterAssertions.AssertEquivalentAsync(noPath, noPathMcp.Value);
        await GraphPathClusterAssertions.AssertEquivalentAsync(noPath, noPathSql);
        await GraphPathClusterAssertions.AssertEquivalentAsync(noPathSql, noPathSqlMcp.Value);
        var depthLimited = GraphPathClusterScenario.Request(seed, maxDepth: 1,
            labels: ImmutableArray.Create(GraphPathClusterScenario.Label));
        var depthResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(depthLimited, cancellationToken));
        var depthMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, depthLimited, cancellationToken));
        await GraphPathClusterAssertions.AssertNoPathAsync(depthResult);
        await GraphPathClusterAssertions.AssertEquivalentAsync(depthResult, depthMcp.Value);
        var depthSql = GraphPathClusterScenario.SqlRequest(seed, maxDepth: 1, label: GraphPathClusterScenario.Label);
        var depthSqlResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(depthSql, cancellationToken));
        var depthSqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.SqlTool, depthSql, cancellationToken));
        await GraphPathClusterAssertions.AssertNoPathAsync(depthSqlResult);
        await GraphPathClusterAssertions.AssertEquivalentAsync(depthSqlResult, depthSqlMcp.Value);
    }

    private static async Task AssertRequestErrorAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        GraphShortestPathRequest request, ErrorCode expected, CancellationToken cancellationToken)
    {
        await GraphPathClusterAssertions.AssertSdkErrorAsync(await sdk.ShortestPathAsync(request, cancellationToken), expected);
        await GraphPathClusterAssertions.AssertMcpErrorAsync(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, request, cancellationToken), expected);
    }

    private static async Task AssertSqlRequestErrorAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlGraphPathRequest request, ErrorCode expected, CancellationToken cancellationToken)
    {
        await GraphPathClusterAssertions.AssertSdkErrorAsync(await sdk.ShortestPathSqlAsync(request, cancellationToken), expected);
        await GraphPathClusterAssertions.AssertMcpErrorAsync(await mcp.CallAsync(
            GraphPathClusterScenario.SqlTool, request, cancellationToken), expected);
    }
}
