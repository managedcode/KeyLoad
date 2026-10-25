using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.GraphTraversal;

/// <summary>AC-GRAPH-007: persisted graph grants and row visibility constrain current public path reads.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphPathClusterAuthorizationTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcGraph007HiddenVerticesAndFieldUseGrantAreConsistentAcrossSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        {
            var seed = await GraphPathClusterScenario.CreateAsync(fixture, deadline.Token);
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
            var sdk = new KeyLoadClient(http, seed.Reader.Secret, IntegrationClientOptions.Execution());
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
                seed.Reader.Secret, deadline.Token);
            await VerifySourceAndTargetHidingAsync(seed, sdk, mcp, deadline.Token);
            var labeled = GraphPathClusterScenario.Request(seed, labels: ImmutableArray.Create(GraphPathClusterScenario.Label));
            await AssertDeniedAsync(sdk, mcp, labeled, ErrorCode.PermissionDenied, deadline.Token);
            var sqlDenied = GraphPathClusterScenario.SqlRequest(seed, label: GraphPathClusterScenario.Label);
            await GraphPathClusterAssertions.AssertSdkErrorAsync(await sdk.ShortestPathSqlAsync(sqlDenied, deadline.Token),
                ErrorCode.PermissionDenied);
            await GraphPathClusterAssertions.AssertMcpErrorAsync(await mcp.CallAsync(
                GraphPathClusterScenario.SqlTool, sqlDenied, deadline.Token), ErrorCode.PermissionDenied);
            await GrantAndVerifyLabelPathAsync(fixture, seed, sdk, mcp, deadline.Token);
        }
        await GraphPathFieldUseR43Trial.RunAsync(fixture, deadline.Token);
    }

    [Test]
    public async Task AcGraph007PersistedGraphCapabilityRevocationDeniesSdkAndMcpThenRestorationWorks()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await GraphPathClusterScenario.GrantLabelUseAsync(fixture, seed, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        var request = GraphPathClusterScenario.Request(seed, labels: ImmutableArray.Create(GraphPathClusterScenario.Label));
        var initial = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(request, deadline.Token));
        await GraphPathClusterAssertions.AssertPathAsync(initial,
            seed.StoredVertices([seed.Source, seed.FirstBranch, seed.Target]), ["a-branch-a", "a-end-a"], GraphPathClusterScenario.Label);

        var deniedGrants = GraphPathClusterScenario.FullGraphGrants(seed.Partition)
            .Select(grant => grant.Resource == GraphPathClusterScenario.Graph
                ? grant with
                {
                    Capabilities = grant.Capabilities & ~Capability.GraphRead
                } : grant)
            .ToArray();
        identity = await GraphPathClusterScenario.ReplaceGrantsAsync(fixture, identity, deniedGrants,
            [GraphPathClusterScenario.LabelUseGrant, GraphPathClusterScenario.LabelReadGrant], deadline.Token);
        await AssertDeniedAsync(sdk, mcp, request, ErrorCode.PermissionDenied, deadline.Token);

        _ = await GraphPathClusterScenario.ReplaceGrantsAsync(fixture, identity,
            GraphPathClusterScenario.FullGraphGrants(seed.Partition), [GraphPathClusterScenario.LabelUseGrant, GraphPathClusterScenario.LabelReadGrant], deadline.Token);
        var recovered = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(request, deadline.Token));
        await GraphPathClusterAssertions.AssertPathAsync(recovered,
            seed.StoredVertices([seed.Source, seed.FirstBranch, seed.Target]), ["a-branch-a", "a-end-a"], GraphPathClusterScenario.Label);
        await Assert.That(recovered.CutPosition).IsGreaterThan(initial.CutPosition);
        var mcpRecovered = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, request, deadline.Token));
        await GraphPathClusterAssertions.AssertEquivalentAsync(recovered, mcpRecovered.Value);
    }

    private static async Task VerifySourceAndTargetHidingAsync(GraphPathClusterSeed seed,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken cancellationToken)
    {
        var hiddenSource = GraphPathClusterScenario.Request(seed, seed.HiddenSource, seed.Target);
        await AssertDeniedAsync(sdk, mcp, hiddenSource, ErrorCode.NotFound, cancellationToken);
        var hiddenTarget = GraphPathClusterScenario.Request(seed, seed.Source, seed.HiddenTarget);
        var targetResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(hiddenTarget, cancellationToken));
        await GraphPathClusterAssertions.AssertNoPathAsync(targetResult);
        var hiddenMiddle = GraphPathClusterScenario.Request(seed, seed.Source, seed.Isolated);
        var middleResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(hiddenMiddle, cancellationToken));
        await GraphPathClusterAssertions.AssertNoPathAsync(middleResult);
        var absent = GraphPathClusterScenario.Request(seed, seed.Source,
            new EntityRef(seed.Partition, GraphPathClusterScenario.CollectionB, "missing-target"));
        var absentResult = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, absent, cancellationToken));
        await GraphPathClusterAssertions.AssertNoPathAsync(absentResult.Value);
    }

    private static async Task GrantAndVerifyLabelPathAsync(ClusterFixture fixture, GraphPathClusterSeed seed,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken cancellationToken)
    {
        _ = await GraphPathClusterScenario.GrantLabelUseAsync(fixture, seed, cancellationToken);
        var request = GraphPathClusterScenario.Request(seed, labels: ImmutableArray.Create(GraphPathClusterScenario.Label));
        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(request, cancellationToken));
        var mcpResult = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, request, cancellationToken));
        await GraphPathClusterAssertions.AssertPathAsync(sdkResult,
            seed.StoredVertices([seed.Source, seed.FirstBranch, seed.Target]), ["a-branch-a", "a-end-a"], GraphPathClusterScenario.Label);
        await GraphPathClusterAssertions.AssertProjectedAsync(sdkResult);
        await GraphPathClusterAssertions.AssertEquivalentAsync(sdkResult, mcpResult.Value);
        var sql = GraphPathClusterScenario.SqlRequest(seed, label: GraphPathClusterScenario.Label);
        var sqlSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(sql, cancellationToken));
        var sqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.SqlTool, sql, cancellationToken));
        await GraphPathClusterAssertions.AssertEquivalentAsync(sdkResult, sqlSdk);
        await GraphPathClusterAssertions.AssertEquivalentAsync(sqlSdk, sqlMcp.Value);
    }

    private static async Task AssertDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        GraphShortestPathRequest request, ErrorCode expected, CancellationToken cancellationToken)
    {
        await GraphPathClusterAssertions.AssertSdkErrorAsync(await sdk.ShortestPathAsync(request, cancellationToken), expected);
        await GraphPathClusterAssertions.AssertMcpErrorAsync(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, request, cancellationToken), expected);
    }
}
