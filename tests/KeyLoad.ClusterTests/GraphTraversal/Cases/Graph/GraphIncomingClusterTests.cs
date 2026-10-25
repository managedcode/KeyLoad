using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphIncomingClusterTests(ClusterFixture fixture)
{
    private const string FirstEdgeId = "same-edge";
    private const string LocalEdgeId = "local-edge";
    private const string UpdatedMarker = "incoming-updated-public";

    [Test]
    public async Task IncomingReadProjectsLocalAndDeliveredSameIdRowsThroughSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphIncomingClusterScenario.CreateAsync(fixture, deadline.Token);
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await SeedAndDeliverAsync(admin, seed, deadline.Token);
        using var readerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var reader = new KeyLoadClient(readerHttp, seed.Reader.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            seed.Reader.Secret, deadline.Token);
        var request = GraphIncomingClusterScenario.Request(seed, limit: 3);

        var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(request,
            deadline.Token));
        var mcpReply = await mcp.CallAsync(GraphIncomingClusterScenario.Tool, request, deadline.Token);
        var mcpPage = await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPage>(mcpReply);

        await GraphIncomingClusterAssertions.AssertThreeRowsAsync(sdkPage, seed);
        await GraphIncomingClusterAssertions.AssertEquivalentAsync(sdkPage, mcpPage.Value);
        await GraphIncomingClusterAssertions.AssertNoPrivatePayloadAsync(mcpReply, seed.Reader.Secret);
        await AssertBudgetFailureAsync(reader, mcp, request, deadline.Token);
        var following = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
            request, deadline.Token));
        await GraphIncomingClusterAssertions.AssertThreeRowsAsync(following, seed);
    }

    [Test]
    public async Task PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphIncomingClusterScenario.CreateAsync(fixture, deadline.Token);
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await SeedAndDeliverAsync(admin, seed, deadline.Token);
        var request = GraphIncomingClusterScenario.Request(seed);
        {
            using var sourceRestrictedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var sourceRestricted = new KeyLoadClient(sourceRestrictedHttp, seed.TargetOnlyReader.Secret, IntegrationClientOptions.Execution());
            await using var sourceMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
                seed.TargetOnlyReader.Secret, deadline.Token);

            var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await sourceRestricted.IncomingEdgesAsync(
                request, deadline.Token));
            var hiddenReply = await sourceMcp.CallAsync(GraphIncomingClusterScenario.Tool, request, deadline.Token);
            var hiddenPage = await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPage>(hiddenReply);
            await GraphIncomingClusterAssertions.AssertSourceHiddenAsync(sdkPage, seed);
            await GraphIncomingClusterAssertions.AssertEquivalentAsync(sdkPage, hiddenPage.Value);
            await GraphIncomingClusterAssertions.AssertNoPrivatePayloadAsync(hiddenReply, seed.TargetOnlyReader.Secret);
            await AssertTargetGraphDeniedAsync(fixture, admin, seed, request, deadline.Token);
        }
        await GraphIncomingPublicContinuation.RunAsync(fixture, admin, seed, deadline.Token);
    }

    [Test]
    public async Task DeleteAndReinsertKeepRevisionedReverseProjectionFromResurrectingStaleRows()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphIncomingClusterScenario.CreateAsync(fixture, deadline.Token);
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await CreateLocalAndCrossEdgesAsync(admin, seed, deadline.Token);
        await GraphIncomingClusterScenario.DeliverAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, deadline.Token);
        await GraphIncomingClusterScenario.DeliverAsync(admin, seed.SecondSourcePartition, seed.Target,
            FirstEdgeId, 1, deadline.Token);
        var updated = await GraphIncomingClusterScenario.CommitAsync(admin, seed.FirstSourcePartition,
            deadline.Token, GraphIncomingClusterScenario.Edge(FirstEdgeId, seed.FirstSource, seed.Target,
                marker: UpdatedMarker, expectedRevision: 1));
        await Assert.That(updated).IsEqualTo(2L);
        await AssertNoFirstCrossEdgeAsync(fixture, seed, deadline.Token);
        var deleted = await GraphIncomingClusterScenario.CommitAsync(admin, seed.FirstSourcePartition,
            deadline.Token, new DeleteEdge(seed.Graph, FirstEdgeId, ExpectedRevision: 2));
        await Assert.That(deleted).IsEqualTo(3L);
        await AssertNoFirstCrossEdgeAsync(fixture, seed, deadline.Token);
        await GraphIncomingClusterScenario.DeliverAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 3, deadline.Token);
        await GraphIncomingClusterScenario.CommitAsync(admin, seed.TargetPartition, deadline.Token,
            new ApplyCrossPartitionReverseEdge(seed.FirstSourcePartition, seed.Graph, FirstEdgeId,
                seed.Target, 2));

        using var readerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var reader = new KeyLoadClient(readerHttp, seed.Reader.Secret, IntegrationClientOptions.Execution());
        var afterDelete = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
            GraphIncomingClusterScenario.Request(seed), deadline.Token));
        await Assert.That(afterDelete.Rows.Any(row => row.Edge.From == seed.Stored(seed.FirstSource))).IsFalse();
        var reinserted = await GraphIncomingClusterScenario.CommitAsync(admin, seed.FirstSourcePartition,
            deadline.Token, GraphIncomingClusterScenario.Edge(FirstEdgeId, seed.FirstSource, seed.Target,
                marker: UpdatedMarker, expectedRevision: 3));
        await Assert.That(reinserted).IsEqualTo(4L);
        await GraphIncomingClusterScenario.DeliverAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 4, deadline.Token);
        await GraphIncomingClusterScenario.CommitAsync(admin, seed.TargetPartition, deadline.Token,
            new ApplyCrossPartitionReverseEdge(seed.FirstSourcePartition, seed.Graph, FirstEdgeId,
                seed.Target, 3));
        var current = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
            GraphIncomingClusterScenario.Request(seed), deadline.Token));
        await GraphIncomingClusterAssertions.AssertExpectedRowAsync(current, seed.Stored(seed.FirstSource),
            seed.Stored(seed.Target), FirstEdgeId, 4, 4, UpdatedMarker);
        await AssertSecondSourceAndLocalRemainAsync(current, seed);
    }

    private static async Task SeedAndDeliverAsync(KeyLoadClient admin, GraphIncomingClusterSeed seed,
        CancellationToken cancellationToken)
    {
        await CreateLocalAndCrossEdgesAsync(admin, seed, cancellationToken);
        await GraphIncomingClusterScenario.ApplyAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
        await GraphIncomingClusterScenario.ApplyAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
        await GraphIncomingClusterScenario.CompleteAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
        await GraphIncomingClusterScenario.DeliverAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
        await GraphIncomingClusterScenario.DeliverAsync(admin, seed.SecondSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
    }

    private static async Task CreateLocalAndCrossEdgesAsync(KeyLoadClient admin,
        GraphIncomingClusterSeed seed, CancellationToken cancellationToken)
    {
        _ = await GraphIncomingClusterScenario.CommitAsync(admin, seed.TargetPartition, cancellationToken,
            GraphIncomingClusterScenario.Edge(LocalEdgeId, seed.LocalSource, seed.Target,
                marker: GraphIncomingClusterScenario.PublicMarker + "-local"));
        _ = await GraphIncomingClusterScenario.CommitAsync(admin, seed.FirstSourcePartition, cancellationToken,
            GraphIncomingClusterScenario.Edge(FirstEdgeId, seed.FirstSource, seed.Target,
                marker: GraphIncomingClusterScenario.PublicMarker + "-first"));
        _ = await GraphIncomingClusterScenario.CommitAsync(admin, seed.SecondSourcePartition, cancellationToken,
            GraphIncomingClusterScenario.Edge(FirstEdgeId, seed.SecondSource, seed.Target,
                marker: GraphIncomingClusterScenario.PublicMarker + "-second"));
    }

    private static async Task AssertBudgetFailureAsync(KeyLoadClient reader, McpOfficialClient mcp,
        ReadIncomingGraphEdgesRequest request, CancellationToken cancellationToken)
    {
        var limited = request with
        {
            Limit = 2
        };
        var sdk = await reader.IncomingEdgesAsync(limited, cancellationToken);
        await GraphIncomingClusterAssertions.AssertFailureAsync(sdk, ErrorCode.BudgetExceeded);
        var reply = await mcp.CallAsync(GraphIncomingClusterScenario.Tool, limited, cancellationToken);
        await GraphIncomingClusterAssertions.AssertMcpFailureAsync(reply, ErrorCode.BudgetExceeded);
    }

    private static async Task AssertNoFirstCrossEdgeAsync(ClusterFixture fixture,
        GraphIncomingClusterSeed seed, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var reader = new KeyLoadClient(http, seed.Reader.Secret, IntegrationClientOptions.Execution());
        var page = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
            GraphIncomingClusterScenario.Request(seed), cancellationToken));
        await Assert.That(page.Rows.Any(row => row.Edge.From == seed.Stored(seed.FirstSource))).IsFalse();
        await Assert.That(page.Rows.Any(row => row.Edge.From == seed.Stored(seed.SecondSource))).IsTrue();
        await Assert.That(page.Rows.Any(row => row.Edge.From == seed.Stored(seed.LocalSource))).IsTrue();
    }

    private static async Task AssertSecondSourceAndLocalRemainAsync(GraphIncomingEdgesPage page,
        GraphIncomingClusterSeed seed)
    {
        await GraphIncomingClusterAssertions.AssertExpectedRowAsync(page, seed.Stored(seed.SecondSource),
            seed.Stored(seed.Target), FirstEdgeId, 1, 1, GraphIncomingClusterScenario.PublicMarker + "-second");
        await GraphIncomingClusterAssertions.AssertExpectedRowAsync(page, seed.Stored(seed.LocalSource),
            seed.Stored(seed.Target), LocalEdgeId, 1, 0, GraphIncomingClusterScenario.PublicMarker + "-local");
    }

    private static async Task AssertTargetGraphDeniedAsync(ClusterFixture fixture, KeyLoadClient admin,
        GraphIncomingClusterSeed seed, ReadIncomingGraphEdgesRequest request, CancellationToken cancellationToken)
    {
        var identity = await GraphIncomingClusterScenario.CreateDocumentOnlyReaderAsync(admin, seed,
            cancellationToken);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var reader = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        var denied = await reader.IncomingEdgesAsync(request, cancellationToken);
        await GraphIncomingClusterAssertions.AssertFailureAsync(denied, ErrorCode.PermissionDenied);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, cancellationToken);
        var reply = await mcp.CallAsync(GraphIncomingClusterScenario.Tool, request, cancellationToken);
        await GraphIncomingClusterAssertions.AssertMcpFailureAsync(reply, ErrorCode.PermissionDenied);
        await GraphIncomingClusterAssertions.AssertNoPrivatePayloadAsync(reply, identity.Secret);
    }
}
