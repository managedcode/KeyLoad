using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ThreeWayHybridRf3Tests(ClusterFixture fixture)
{
    private const double Tolerance = 0.000000000001;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcSearchExplainActualSdkOfficialMcpAndQ1CallReturnCompleteLiteralContributions(bool restricted)
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await ThreeWayHybridRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, vectorGrant: true, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var original = scenario.Request(restricted, expansion: false);
        var request = original with { Search = original.Search with { Explain = true } };
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(request, deadline.Token));
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            ThreeWayHybridRf3Scenario.SearchGraphTool, request, deadline.Token));
        var call = SqlRf3Protocol.Call(scenario.Partition, ThreeWayHybridRf3Scenario.SearchGraphTool, request);
        await HybridExplainRf3Assertions.LiteralAsync(direct, scenario.Partition, restricted);
        await HybridExplainRf3Assertions.LiteralAsync(official.Value, scenario.Partition, restricted);
        await HybridExplainRf3Assertions.LiteralAsync(await SqlRf3Protocol.SdkAsync<GraphSearchResult>(
            sdk, call, deadline.Token), scenario.Partition, restricted);
        await HybridExplainRf3Assertions.LiteralAsync(await SqlRf3Protocol.McpAsync<GraphSearchResult>(
            mcp, call, deadline.Token), scenario.Partition, restricted);
    }

    [Test]
    public async Task AcGsearch003DirectAndSqlSdkAndOfficialMcpMatchIndependentThreeBranchOracle()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await ThreeWayHybridRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, vectorGrant: true, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var directRequest = scenario.Request(expansion: true);
        var sqlRequest = scenario.SqlRequest(expansion: true);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(directRequest, deadline.Token));
        var sql = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchSqlAsync(sqlRequest, deadline.Token));
        var directMcp = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            ThreeWayHybridRf3Scenario.SearchGraphTool, directRequest, deadline.Token));
        var sqlMcp = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            ThreeWayHybridRf3Scenario.SqlGraphTool, sqlRequest, deadline.Token));
        await VerifyEquivalentAsync(direct, sql, directMcp.Value, sqlMcp.Value, restricted: false);
        await ThreeWayHybridRf3Assertions.AssertExpansionAsync(direct);
    }

    [Test]
    public async Task AcGsearch003ScopeAndAllowlistRerankBeforeFusionAndExpansionDoesNotChangeHits()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await ThreeWayHybridRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, vectorGrant: true, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            identity.Secret, deadline.Token);
        var directRequest = scenario.Request(restricted: true, expansion: false);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(directRequest, deadline.Token));
        var sqlRequest = scenario.SqlRequest(restricted: true, expansion: false);
        var sql = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchSqlAsync(sqlRequest, deadline.Token));
        var official = await McpCallerAssertions.SuccessAsync<GraphSearchResult>(await mcp.CallAsync(
            ThreeWayHybridRf3Scenario.SqlGraphTool, sqlRequest, deadline.Token));
        await ThreeWayHybridRf3Assertions.AssertWeightedResultAsync(direct, restricted: true);
        await GraphSearchRf3Assertions.AssertEquivalentAsync(direct, sql);
        await GraphSearchRf3Assertions.AssertEquivalentAsync(sql, official.Value);

        var expanded = await McpCallerAssertions.SdkSuccessAsync(await sdk.GraphSearchAsync(
            scenario.Request(restricted: true, expansion: true), deadline.Token));
        await ThreeWayHybridRf3Assertions.AssertWeightedResultAsync(expanded, restricted: true);
        await ThreeWayHybridRf3Assertions.AssertExpansionAsync(expanded);
        await AssertSameHitsAsync(direct, expanded);
    }

    [Test]
    public async Task AcGsearch003ZeroWeightsDoNotSkipPersistedAuthorizationOrRevocation()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await ThreeWayHybridRf3Scenario.CreateAsync(fixture, deadline.Token);
        var allowed = await scenario.CreateReaderAsync(fixture, vectorGrant: true, deadline.Token);
        var denied = await scenario.CreateReaderAsync(fixture, vectorGrant: false, deadline.Token);
        using var allowedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        using var deniedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var allowedSdk = new KeyLoadClient(allowedHttp, allowed.Secret, IntegrationClientOptions.Execution());
        var deniedSdk = new KeyLoadClient(deniedHttp, denied.Secret, IntegrationClientOptions.Execution());
        await using var allowedMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            allowed.Secret, deadline.Token);
        await using var deniedMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            denied.Secret, deadline.Token);
        var zeroDirect = scenario.Request(allZero: true, expansion: false) with
        { Search = scenario.Request(allZero: true, expansion: false).Search with { AllowedIds = ImmutableArray<string>.Empty } };
        var zeroSql = scenario.SqlRequest(allZero: true, expansion: false);
        var authorized = await McpCallerAssertions.SdkSuccessAsync(await allowedSdk.GraphSearchAsync(zeroDirect, deadline.Token));
        await Assert.That(authorized.Hits).IsEmpty();
        var authorizedSql = await McpCallerAssertions.SdkSuccessAsync(await allowedSdk.SearchSqlAsync(zeroSql, deadline.Token));
        await Assert.That(authorizedSql.Hits).IsEmpty();
        await AssertVectorPermissionDeniedAsync(deniedSdk, deniedMcp, scenario, deadline.Token);
        await ThreeWayHybridRf3Scenario.RevokeAsync(fixture, allowed, deadline.Token);
        await AssertRevokedAsync(allowedSdk, allowedMcp, zeroDirect, zeroSql, deadline.Token);
    }

    [Test]
    public async Task AcGsearch003ZeroWeightGraphLabelsRequirePersistedFieldUse()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await ThreeWayHybridRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, vectorGrant: true, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        await AssertLabelUseDeniedAsync(sdk, mcp, scenario, deadline.Token);
    }

    private static async Task AssertLabelUseDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ThreeWayHybridRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var direct = await sdk.GraphSearchAsync(scenario.Request(allZero: true, expansion: false, labeled: true),
            cancellationToken);
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var directMcp = await mcp.CallAsync(ThreeWayHybridRf3Scenario.SearchGraphTool,
            scenario.Request(allZero: true, expansion: false, labeled: true), cancellationToken);
        await McpCallerAssertions.ErrorAsync(directMcp, ErrorCode.PermissionDenied, dispatched: true);
        var sqlRequest = scenario.SqlRequest(allZero: true, expansion: false, labeled: true);
        var sql = await sdk.SearchSqlAsync(sqlRequest, cancellationToken);
        await Assert.That(sql.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var sqlMcp = await mcp.CallAsync(ThreeWayHybridRf3Scenario.SqlGraphTool, sqlRequest, cancellationToken);
        await McpCallerAssertions.ErrorAsync(sqlMcp, ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task VerifyEquivalentAsync(GraphSearchResult direct, GraphSearchResult sql,
        GraphSearchResult directMcp, GraphSearchResult sqlMcp, bool restricted)
    {
        await ThreeWayHybridRf3Assertions.AssertWeightedResultAsync(direct, restricted);
        await GraphSearchRf3Assertions.AssertEquivalentAsync(direct, sql);
        await GraphSearchRf3Assertions.AssertEquivalentAsync(direct, directMcp);
        await GraphSearchRf3Assertions.AssertEquivalentAsync(direct, sqlMcp);
        for (var index = 0; index < direct.Hits.Length; index++)
        {
            await Assert.That(direct.Hits[index].Score).IsEqualTo(directMcp.Hits[index].Score).Within(Tolerance);
        }
    }

    private static async Task AssertSameHitsAsync(GraphSearchResult left, GraphSearchResult right)
    {
        await Assert.That(left.Hits.Length).IsEqualTo(right.Hits.Length);
        for (var index = 0; index < left.Hits.Length; index++)
        {
            await Assert.That(left.Hits[index].Document.Reference.Id)
                .IsEqualTo(right.Hits[index].Document.Reference.Id);
            await Assert.That(left.Hits[index].Score).IsEqualTo(right.Hits[index].Score).Within(Tolerance);
        }
    }

    private static async Task AssertVectorPermissionDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ThreeWayHybridRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var direct = scenario.Request(allZero: true, expansion: false) with
        { Search = scenario.Request(allZero: true, expansion: false).Search with { AllowedIds = ImmutableArray<string>.Empty } };
        var sdkDirect = await sdk.GraphSearchAsync(direct, cancellationToken);
        await Assert.That(sdkDirect.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(ThreeWayHybridRf3Scenario.SearchGraphTool,
            direct, cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
        var sql = scenario.SqlRequest(allZero: true, expansion: false);
        var sdkSql = await sdk.SearchSqlAsync(sql, cancellationToken);
        await Assert.That(sdkSql.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(ThreeWayHybridRf3Scenario.SqlGraphTool,
            sql, cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task AssertRevokedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        GraphSearchRequest direct, SqlGraphSearchRequest sql, CancellationToken cancellationToken)
    {
        var result = await sdk.GraphSearchAsync(direct, cancellationToken);
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var sqlResult = await sdk.SearchSqlAsync(sql, cancellationToken);
        await Assert.That(sqlResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => mcp.CallAsync(
            ThreeWayHybridRf3Scenario.SearchGraphTool, direct, cancellationToken));
        await Assert.That(failure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
        var sqlFailure = await Assert.ThrowsAsync<HttpRequestException>(() => mcp.CallAsync(
            ThreeWayHybridRf3Scenario.SqlGraphTool, sql, cancellationToken));
        await Assert.That(sqlFailure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
    }
}
