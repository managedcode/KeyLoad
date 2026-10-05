using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

/// <summary>AC-GRAPH-009: actual SDK and official MCP expose the same bounded shortest-path result.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphPathRf3ParityTests(ClusterFixture fixture)
{
    private const string ForeignPartitionKey = "key";
    [Test]
    public async Task AcGraph009DirectAndSqlSdkMcpReturnExactTiedPathAndProjectedEdges()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await GraphPathRf3Scenario.GrantLabelUseAsync(fixture, seed, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var direct = GraphPathRf3Scenario.Request(seed, labels: ImmutableArray.Create(GraphPathRf3Scenario.Label));
        var sql = GraphPathRf3Scenario.SqlRequest(seed, label: GraphPathRf3Scenario.Label);

        var sdkDirect = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(direct, deadline.Token));
        var mcpDirect = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, direct, deadline.Token));
        var sdkSql = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(sql, deadline.Token));
        var mcpSql = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.SqlTool, sql, deadline.Token));
        var vertices = new[] { seed.Source, seed.FirstBranch, seed.Target };
        var edges = new[] { "a-branch-a", "a-end-a" };

        await GraphPathRf3Assertions.AssertPathAsync(sdkDirect, vertices, edges, GraphPathRf3Scenario.Label);
        await GraphPathRf3Assertions.AssertProjectedAsync(sdkDirect);
        await GraphPathRf3Assertions.AssertEquivalentAsync(sdkDirect, mcpDirect.Value);
        await GraphPathRf3Assertions.AssertEquivalentAsync(sdkDirect, sdkSql);
        await GraphPathRf3Assertions.AssertEquivalentAsync(sdkDirect, mcpSql.Value);
    }

    [Test]
    public async Task AcGraph009UnrestrictedDirectPathAndZeroHopMatchOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Reader.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            seed.Reader.Secret, deadline.Token);
        var direct = GraphPathRf3Scenario.Request(seed);
        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(direct, deadline.Token));
        var mcpResult = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, direct, deadline.Token));
        await GraphPathRf3Assertions.AssertPathAsync(sdkResult, [seed.Source, seed.Target], ["z-shortcut"], "shortcut");
        await GraphPathRf3Assertions.AssertEquivalentAsync(sdkResult, mcpResult.Value);
        var sql = GraphPathRf3Scenario.SqlRequest(seed);
        var sqlSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(sql, deadline.Token));
        var sqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.SqlTool, sql, deadline.Token));
        await GraphPathRf3Assertions.AssertEquivalentAsync(sdkResult, sqlSdk);
        await GraphPathRf3Assertions.AssertEquivalentAsync(sqlSdk, sqlMcp.Value);

        var zero = GraphPathRf3Scenario.Request(seed, seed.Source, seed.Source, maxDepth: 0);
        var zeroSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(zero, deadline.Token));
        var zeroMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, zero, deadline.Token));
        await Assert.That(zeroSdk.Found).IsTrue();
        await Assert.That(zeroSdk.Hops).IsEqualTo(0);
        await Assert.That(zeroSdk.Vertices.SequenceEqual([seed.Source])).IsTrue();
        await Assert.That(zeroSdk.Edges).IsEmpty();
        await GraphPathRf3Assertions.AssertEquivalentAsync(zeroSdk, zeroMcp.Value);
        var zeroSql = GraphPathRf3Scenario.SqlRequest(seed, seed.Source, seed.Source, maxDepth: 0);
        var zeroSqlSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(zeroSql, deadline.Token));
        var zeroSqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.SqlTool, zeroSql, deadline.Token));
        await GraphPathRf3Assertions.AssertEquivalentAsync(zeroSdk, zeroSqlSdk);
        await GraphPathRf3Assertions.AssertEquivalentAsync(zeroSqlSdk, zeroSqlMcp.Value);
    }

    [Test]
    public async Task AcGraph009ForeignPartitionAndInvalidRequestFailClosedAcrossSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, seed.Reader.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            seed.Reader.Secret, deadline.Token);
        var request = GraphPathRf3Scenario.Request(seed);
        await AssertRequestErrorAsync(sdk, mcp, request with { Version = 2 }, ErrorCode.Validation, deadline.Token);
        await AssertSqlRequestErrorAsync(sdk, mcp, GraphPathRf3Scenario.SqlRequest(seed) with { Version = 2 },
            ErrorCode.Validation, deadline.Token);
        var foreignPartition = new PartitionRef("foreign", "db", "domain", ForeignPartitionKey);
        var foreign = request with { To = request.To with { Partition = foreignPartition } };
        await AssertRequestErrorAsync(sdk, mcp, foreign, ErrorCode.UnsupportedCapability, deadline.Token);
        // SQL scopes the whole statement with Query.Partition; endpoint literals have no partition argument.
        var foreignSqlScope = GraphPathRf3Scenario.SqlRequestInPartition(seed, foreignPartition);
        await AssertSqlRequestErrorAsync(sdk, mcp, foreignSqlScope,
            ErrorCode.PermissionDenied, deadline.Token);
        await AssertRequestErrorAsync(sdk, mcp, request with { MaxDepth = 17 }, ErrorCode.BudgetExceeded, deadline.Token);
        await AssertRequestErrorAsync(sdk, mcp, request with { MaxVertices = 10_001 },
            ErrorCode.BudgetExceeded, deadline.Token);
        await AssertRequestErrorAsync(sdk, mcp, request with { MaxEdges = 50_001 },
            ErrorCode.BudgetExceeded, deadline.Token);
        await AssertSqlRequestErrorAsync(sdk, mcp,
            GraphPathRf3Scenario.SqlRequest(seed, maxDepth: 17), ErrorCode.BudgetExceeded, deadline.Token);
        _ = await GraphPathRf3Scenario.GrantLabelUseAsync(fixture, seed, deadline.Token);
        await VerifyNoPathAndDepthAsync(seed, sdk, mcp, deadline.Token);
    }

    private static async Task VerifyNoPathAndDepthAsync(GraphPathRf3Seed seed, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken cancellationToken)
    {
        var noPathRequest = GraphPathRf3Scenario.Request(seed, to: seed.Isolated);
        var noPath = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(noPathRequest, cancellationToken));
        var noPathMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, noPathRequest, cancellationToken));
        var noPathSql = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(
            GraphPathRf3Scenario.SqlRequest(seed, to: seed.Isolated), cancellationToken));
        var noPathSqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.SqlTool, GraphPathRf3Scenario.SqlRequest(seed, to: seed.Isolated), cancellationToken));
        await GraphPathRf3Assertions.AssertNoPathAsync(noPath);
        await GraphPathRf3Assertions.AssertEquivalentAsync(noPath, noPathMcp.Value);
        await GraphPathRf3Assertions.AssertEquivalentAsync(noPath, noPathSql);
        await GraphPathRf3Assertions.AssertEquivalentAsync(noPathSql, noPathSqlMcp.Value);
        var depthLimited = GraphPathRf3Scenario.Request(seed, maxDepth: 1,
            labels: ImmutableArray.Create(GraphPathRf3Scenario.Label));
        var depthResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(depthLimited, cancellationToken));
        var depthMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, depthLimited, cancellationToken));
        await GraphPathRf3Assertions.AssertNoPathAsync(depthResult);
        await GraphPathRf3Assertions.AssertEquivalentAsync(depthResult, depthMcp.Value);
        var depthSql = GraphPathRf3Scenario.SqlRequest(seed, maxDepth: 1, label: GraphPathRf3Scenario.Label);
        var depthSqlResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(depthSql, cancellationToken));
        var depthSqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.SqlTool, depthSql, cancellationToken));
        await GraphPathRf3Assertions.AssertNoPathAsync(depthSqlResult);
        await GraphPathRf3Assertions.AssertEquivalentAsync(depthSqlResult, depthSqlMcp.Value);
    }

    private static async Task AssertRequestErrorAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        GraphShortestPathRequest request, ErrorCode expected, CancellationToken cancellationToken)
    {
        await GraphPathRf3Assertions.AssertSdkErrorAsync(await sdk.ShortestPathAsync(request, cancellationToken), expected);
        await GraphPathRf3Assertions.AssertMcpErrorAsync(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, request, cancellationToken), expected);
    }

    private static async Task AssertSqlRequestErrorAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlGraphPathRequest request, ErrorCode expected, CancellationToken cancellationToken)
    {
        await GraphPathRf3Assertions.AssertSdkErrorAsync(await sdk.ShortestPathSqlAsync(request, cancellationToken), expected);
        await GraphPathRf3Assertions.AssertMcpErrorAsync(await mcp.CallAsync(
            GraphPathRf3Scenario.SqlTool, request, cancellationToken), expected);
    }
}
