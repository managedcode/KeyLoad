using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphIncomingRf3Tests(ClusterFixture fixture)
{
    private const string FirstEdgeId = "same-edge";
    private const string LocalEdgeId = "local-edge";
    private const string UpdatedMarker = "incoming-updated-public";

    [Test]
    public async Task IncomingReadProjectsLocalAndDeliveredSameIdRowsThroughSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphIncomingRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey);
        await SeedAndDeliverAsync(admin, seed, deadline.Token);
        using var readerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var reader = new KeyLoadClient(readerHttp, seed.Reader.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            seed.Reader.Secret, deadline.Token);
        var request = GraphIncomingRf3Scenario.Request(seed, limit: 3);

        var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(request,
            deadline.Token));
        var mcpReply = await mcp.CallAsync(GraphIncomingRf3Scenario.Tool, request, deadline.Token);
        var mcpPage = await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPageV1>(mcpReply);

        await GraphIncomingRf3Assertions.AssertThreeRowsAsync(sdkPage, seed);
        await GraphIncomingRf3Assertions.AssertEquivalentAsync(sdkPage, mcpPage.Value);
        await GraphIncomingRf3Assertions.AssertNoPrivatePayloadAsync(mcpReply, seed.Reader.Secret);
        await AssertBudgetFailureAsync(reader, mcp, request, deadline.Token);
        var following = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
            request, deadline.Token));
        await GraphIncomingRf3Assertions.AssertThreeRowsAsync(following, seed);
    }

    [Test]
    public async Task PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphIncomingRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey);
        await SeedAndDeliverAsync(admin, seed, deadline.Token);
        var request = GraphIncomingRf3Scenario.Request(seed);
        using var sourceRestrictedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sourceRestricted = new KeyLoadClient(sourceRestrictedHttp, seed.TargetOnlyReader.Secret);
        await using var sourceMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            seed.TargetOnlyReader.Secret, deadline.Token);

        var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await sourceRestricted.IncomingEdgesAsync(
            request, deadline.Token));
        var hiddenReply = await sourceMcp.CallAsync(GraphIncomingRf3Scenario.Tool, request, deadline.Token);
        var hiddenPage = await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPageV1>(hiddenReply);
        await GraphIncomingRf3Assertions.AssertSourceHiddenAsync(sdkPage, seed);
        await GraphIncomingRf3Assertions.AssertEquivalentAsync(sdkPage, hiddenPage.Value);
        await GraphIncomingRf3Assertions.AssertNoPrivatePayloadAsync(hiddenReply, seed.TargetOnlyReader.Secret);
        await AssertTargetGraphDeniedAsync(fixture, admin, seed, request, deadline.Token);
    }

    [Test]
    public async Task DeleteAndReinsertKeepRevisionedReverseProjectionFromResurrectingStaleRows()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphIncomingRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey);
        await CreateLocalAndCrossEdgesAsync(admin, seed, deadline.Token);
        await GraphIncomingRf3Scenario.DeliverAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, deadline.Token);
        await GraphIncomingRf3Scenario.DeliverAsync(admin, seed.SecondSourcePartition, seed.Target,
            FirstEdgeId, 1, deadline.Token);
        var updated = await GraphIncomingRf3Scenario.CommitAsync(admin, seed.FirstSourcePartition,
            deadline.Token, GraphIncomingRf3Scenario.Edge(FirstEdgeId, seed.FirstSource, seed.Target,
                marker: UpdatedMarker, expectedRevision: 1));
        await Assert.That(updated).IsEqualTo(2L);
        await AssertNoFirstCrossEdgeAsync(fixture, seed, deadline.Token);
        var deleted = await GraphIncomingRf3Scenario.CommitAsync(admin, seed.FirstSourcePartition,
            deadline.Token, new DeleteEdge(seed.Graph, FirstEdgeId, ExpectedRevision: 2));
        await Assert.That(deleted).IsEqualTo(3L);
        await AssertNoFirstCrossEdgeAsync(fixture, seed, deadline.Token);
        await GraphIncomingRf3Scenario.DeliverAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 3, deadline.Token);
        await GraphIncomingRf3Scenario.CommitAsync(admin, seed.TargetPartition, deadline.Token,
            new ApplyCrossPartitionReverseEdge(seed.FirstSourcePartition, seed.Graph, FirstEdgeId,
                seed.Target, 2));

        using var readerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var reader = new KeyLoadClient(readerHttp, seed.Reader.Secret);
        var afterDelete = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
            GraphIncomingRf3Scenario.Request(seed), deadline.Token));
        await Assert.That(afterDelete.Rows.Any(row => row.Edge.From == seed.FirstSource)).IsFalse();
        var reinserted = await GraphIncomingRf3Scenario.CommitAsync(admin, seed.FirstSourcePartition,
            deadline.Token, GraphIncomingRf3Scenario.Edge(FirstEdgeId, seed.FirstSource, seed.Target,
                marker: UpdatedMarker, expectedRevision: 3));
        await Assert.That(reinserted).IsEqualTo(4L);
        await GraphIncomingRf3Scenario.DeliverAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 4, deadline.Token);
        await GraphIncomingRf3Scenario.CommitAsync(admin, seed.TargetPartition, deadline.Token,
            new ApplyCrossPartitionReverseEdge(seed.FirstSourcePartition, seed.Graph, FirstEdgeId,
                seed.Target, 3));
        var current = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
            GraphIncomingRf3Scenario.Request(seed), deadline.Token));
        await GraphIncomingRf3Assertions.AssertExpectedRowAsync(current, seed.FirstSource, seed.Target, FirstEdgeId,
            4, 4, UpdatedMarker);
        await AssertSecondSourceAndLocalRemainAsync(current, seed);
    }

    private static async Task SeedAndDeliverAsync(KeyLoadClient admin, GraphIncomingRf3Seed seed,
        CancellationToken cancellationToken)
    {
        await CreateLocalAndCrossEdgesAsync(admin, seed, cancellationToken);
        await GraphIncomingRf3Scenario.ApplyAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
        await GraphIncomingRf3Scenario.ApplyAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
        await GraphIncomingRf3Scenario.CompleteAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
        await GraphIncomingRf3Scenario.DeliverAsync(admin, seed.FirstSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
        await GraphIncomingRf3Scenario.DeliverAsync(admin, seed.SecondSourcePartition, seed.Target,
            FirstEdgeId, 1, cancellationToken);
    }

    private static async Task CreateLocalAndCrossEdgesAsync(KeyLoadClient admin,
        GraphIncomingRf3Seed seed, CancellationToken cancellationToken)
    {
        _ = await GraphIncomingRf3Scenario.CommitAsync(admin, seed.TargetPartition, cancellationToken,
            GraphIncomingRf3Scenario.Edge(LocalEdgeId, seed.LocalSource, seed.Target,
                marker: GraphIncomingRf3Scenario.PublicMarker + "-local"));
        _ = await GraphIncomingRf3Scenario.CommitAsync(admin, seed.FirstSourcePartition, cancellationToken,
            GraphIncomingRf3Scenario.Edge(FirstEdgeId, seed.FirstSource, seed.Target,
                marker: GraphIncomingRf3Scenario.PublicMarker + "-first"));
        _ = await GraphIncomingRf3Scenario.CommitAsync(admin, seed.SecondSourcePartition, cancellationToken,
            GraphIncomingRf3Scenario.Edge(FirstEdgeId, seed.SecondSource, seed.Target,
                marker: GraphIncomingRf3Scenario.PublicMarker + "-second"));
    }

    private static async Task AssertBudgetFailureAsync(KeyLoadClient reader, McpOfficialClient mcp,
        ReadIncomingGraphEdgesRequestV1 request, CancellationToken cancellationToken)
    {
        var limited = request with { Limit = 2 };
        var sdk = await reader.IncomingEdgesAsync(limited, cancellationToken);
        await GraphIncomingRf3Assertions.AssertFailureAsync(sdk, ErrorCode.BudgetExceeded);
        var reply = await mcp.CallAsync(GraphIncomingRf3Scenario.Tool, limited, cancellationToken);
        await GraphIncomingRf3Assertions.AssertMcpFailureAsync(reply, ErrorCode.BudgetExceeded);
    }

    private static async Task AssertNoFirstCrossEdgeAsync(ClusterFixture fixture,
        GraphIncomingRf3Seed seed, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var reader = new KeyLoadClient(http, seed.Reader.Secret);
        var page = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
            GraphIncomingRf3Scenario.Request(seed), cancellationToken));
        await Assert.That(page.Rows.Any(row => row.Edge.From == seed.FirstSource)).IsFalse();
        await Assert.That(page.Rows.Any(row => row.Edge.From == seed.SecondSource)).IsTrue();
        await Assert.That(page.Rows.Any(row => row.Edge.From == seed.LocalSource)).IsTrue();
    }

    private static async Task AssertSecondSourceAndLocalRemainAsync(GraphIncomingEdgesPageV1 page,
        GraphIncomingRf3Seed seed)
    {
        await GraphIncomingRf3Assertions.AssertExpectedRowAsync(page, seed.SecondSource,
            seed.Target, FirstEdgeId, 1, 1, GraphIncomingRf3Scenario.PublicMarker + "-second");
        await GraphIncomingRf3Assertions.AssertExpectedRowAsync(page, seed.LocalSource,
            seed.Target, LocalEdgeId, 1, 0, GraphIncomingRf3Scenario.PublicMarker + "-local");
    }

    private static async Task AssertTargetGraphDeniedAsync(ClusterFixture fixture, KeyLoadClient admin,
        GraphIncomingRf3Seed seed, ReadIncomingGraphEdgesRequestV1 request, CancellationToken cancellationToken)
    {
        var identity = await GraphIncomingRf3Scenario.CreateDocumentOnlyReaderAsync(admin, seed,
            cancellationToken);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var reader = new KeyLoadClient(http, identity.Secret);
        var denied = await reader.IncomingEdgesAsync(request, cancellationToken);
        await GraphIncomingRf3Assertions.AssertFailureAsync(denied, ErrorCode.PermissionDenied);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, cancellationToken);
        var reply = await mcp.CallAsync(GraphIncomingRf3Scenario.Tool, request, cancellationToken);
        await GraphIncomingRf3Assertions.AssertMcpFailureAsync(reply, ErrorCode.PermissionDenied);
        await GraphIncomingRf3Assertions.AssertNoPrivatePayloadAsync(reply, identity.Secret);
    }
}
