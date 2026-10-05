using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

/// <summary>AC-GRAPH-007: persisted graph grants and row visibility constrain current public path reads.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphPathRf3AuthorizationTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcGraph007HiddenVerticesAndFieldUseGrantAreConsistentAcrossSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Reader.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            seed.Reader.Secret, deadline.Token);
        await VerifySourceAndTargetHidingAsync(seed, sdk, mcp, deadline.Token);
        var labeled = GraphPathRf3Scenario.Request(seed, labels: ImmutableArray.Create(GraphPathRf3Scenario.Label));
        await AssertDeniedAsync(sdk, mcp, labeled, ErrorCode.PermissionDenied, deadline.Token);
        var sqlDenied = GraphPathRf3Scenario.SqlRequest(seed, label: GraphPathRf3Scenario.Label);
        await GraphPathRf3Assertions.AssertSdkErrorAsync(await sdk.ShortestPathSqlAsync(sqlDenied, deadline.Token),
            ErrorCode.PermissionDenied);
        await GraphPathRf3Assertions.AssertMcpErrorAsync(await mcp.CallAsync(
            GraphPathRf3Scenario.SqlTool, sqlDenied, deadline.Token), ErrorCode.PermissionDenied);
        await GrantAndVerifyLabelPathAsync(fixture, seed, sdk, mcp, deadline.Token);
    }

    [Test]
    public async Task AcGraph007PersistedGraphCapabilityRevocationDeniesSdkAndMcpThenRestorationWorks()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await GraphPathRf3Scenario.GrantLabelUseAsync(fixture, seed, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        var request = GraphPathRf3Scenario.Request(seed, labels: ImmutableArray.Create(GraphPathRf3Scenario.Label));
        var initial = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(request, deadline.Token));
        await GraphPathRf3Assertions.AssertPathAsync(initial,
            [seed.Source, seed.FirstBranch, seed.Target], ["a-branch-a", "a-end-a"], GraphPathRf3Scenario.Label);

        var deniedGrants = GraphPathRf3Scenario.FullGraphGrants(seed.Partition)
            .Select(grant => grant.Resource == GraphPathRf3Scenario.Graph
                ? grant with { Capabilities = grant.Capabilities & ~Capability.GraphRead } : grant)
            .ToArray();
        identity = await GraphPathRf3Scenario.ReplaceGrantsAsync(fixture, identity, deniedGrants,
            [GraphPathRf3Scenario.LabelUseGrant, GraphPathRf3Scenario.LabelReadGrant], deadline.Token);
        await AssertDeniedAsync(sdk, mcp, request, ErrorCode.PermissionDenied, deadline.Token);

        _ = await GraphPathRf3Scenario.ReplaceGrantsAsync(fixture, identity,
            GraphPathRf3Scenario.FullGraphGrants(seed.Partition), [GraphPathRf3Scenario.LabelUseGrant, GraphPathRf3Scenario.LabelReadGrant], deadline.Token);
        var recovered = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(request, deadline.Token));
        await GraphPathRf3Assertions.AssertPathAsync(recovered,
            [seed.Source, seed.FirstBranch, seed.Target], ["a-branch-a", "a-end-a"], GraphPathRf3Scenario.Label);
        await Assert.That(recovered.CutPosition).IsGreaterThan(initial.CutPosition);
        var mcpRecovered = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, request, deadline.Token));
        await GraphPathRf3Assertions.AssertEquivalentAsync(recovered, mcpRecovered.Value);
    }

    private static async Task VerifySourceAndTargetHidingAsync(GraphPathRf3Seed seed,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken cancellationToken)
    {
        var hiddenSource = GraphPathRf3Scenario.Request(seed, seed.HiddenSource, seed.Target);
        await AssertDeniedAsync(sdk, mcp, hiddenSource, ErrorCode.NotFound, cancellationToken);
        var hiddenTarget = GraphPathRf3Scenario.Request(seed, seed.Source, seed.HiddenTarget);
        var targetResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(hiddenTarget, cancellationToken));
        await GraphPathRf3Assertions.AssertNoPathAsync(targetResult);
        var hiddenMiddle = GraphPathRf3Scenario.Request(seed, seed.Source, seed.Isolated);
        var middleResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(hiddenMiddle, cancellationToken));
        await GraphPathRf3Assertions.AssertNoPathAsync(middleResult);
        var absent = GraphPathRf3Scenario.Request(seed, seed.Source,
            new EntityRef(seed.Partition, GraphPathRf3Scenario.CollectionB, "missing-target"));
        var absentResult = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, absent, cancellationToken));
        await GraphPathRf3Assertions.AssertNoPathAsync(absentResult.Value);
    }

    private static async Task GrantAndVerifyLabelPathAsync(ClusterFixture fixture, GraphPathRf3Seed seed,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken cancellationToken)
    {
        _ = await GraphPathRf3Scenario.GrantLabelUseAsync(fixture, seed, cancellationToken);
        var request = GraphPathRf3Scenario.Request(seed, labels: ImmutableArray.Create(GraphPathRf3Scenario.Label));
        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathAsync(request, cancellationToken));
        var mcpResult = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, request, cancellationToken));
        await GraphPathRf3Assertions.AssertPathAsync(sdkResult,
            [seed.Source, seed.FirstBranch, seed.Target], ["a-branch-a", "a-end-a"], GraphPathRf3Scenario.Label);
        await GraphPathRf3Assertions.AssertProjectedAsync(sdkResult);
        await GraphPathRf3Assertions.AssertEquivalentAsync(sdkResult, mcpResult.Value);
        var sql = GraphPathRf3Scenario.SqlRequest(seed, label: GraphPathRf3Scenario.Label);
        var sqlSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ShortestPathSqlAsync(sql, cancellationToken));
        var sqlMcp = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.SqlTool, sql, cancellationToken));
        await GraphPathRf3Assertions.AssertEquivalentAsync(sdkResult, sqlSdk);
        await GraphPathRf3Assertions.AssertEquivalentAsync(sqlSdk, sqlMcp.Value);
    }

    private static async Task AssertDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        GraphShortestPathRequest request, ErrorCode expected, CancellationToken cancellationToken)
    {
        await GraphPathRf3Assertions.AssertSdkErrorAsync(await sdk.ShortestPathAsync(request, cancellationToken), expected);
        await GraphPathRf3Assertions.AssertMcpErrorAsync(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, request, cancellationToken), expected);
    }
}
