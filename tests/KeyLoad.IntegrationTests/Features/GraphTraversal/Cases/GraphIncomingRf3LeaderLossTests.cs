using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphIncomingRf3LeaderLossTests(ClusterFixture fixture)
{
    private const string SourceCollection = GraphPathRf3Scenario.CollectionA;
    private const string EdgeId = "incoming-survives-leader-rejoin";
    private const string Projection = "eventual-reverse.v1";
    private const string GuidFormat = "N";
    private const int RequestVersion = 1;

    [Test]
    public async Task DeliveredIncomingProjectionSurvivesOwnedLeaderRestartAndRejoin()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathRf3Scenario.CreateAsync(fixture, deadline.Token);
        seed = seed with
        {
            Reader = await GraphPathRf3Scenario.GrantLabelUseAsync(fixture, seed, deadline.Token)
        };
        var sourcePartition = seed.Partition with { PartitionKey = Guid.NewGuid().ToString(GuidFormat) };
        var source = new EntityRef(sourcePartition, SourceCollection, "remote-source");
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await SeedCrossEdgeAsync(admin, seed, sourcePartition, source, deadline.Token);
        using var clients = new GraphPathRf3NodeClients(fixture, seed.Reader.Secret);

        await GraphPathRf3LeaderLoss.ExecuteAsync(fixture, seed, clients, deadline.Token);
        foreach (var reader in clients.Readers)
        {
            var page = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
                Request(seed), deadline.Token));
            await AssertDeliveredRowAsync(page, seed.Target, source);
        }
        await VerifyOfficialMcpAsync(seed, source, deadline.Token);
    }

    private static ReadIncomingGraphEdgesRequestV1 Request(GraphPathRf3Seed seed)
        => new(RequestVersion, seed.Target, GraphPathRf3Scenario.Graph, 10);

    private static async Task SeedCrossEdgeAsync(KeyLoadClient admin, GraphPathRf3Seed seed,
        PartitionRef sourcePartition, EntityRef source, CancellationToken cancellationToken)
    {
        var seedCommand = new CommandRequest(Guid.NewGuid(), sourcePartition,
            [new PutDocument(SourceCollection, source.Id, GraphIncomingRf3Scenario.EmptyJson,
                Access: new(seed.Reader.Principal.Id))]);
        _ = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(seedCommand, cancellationToken))
            .ConfigureAwait(false);
        var sourceCommand = new CommandRequest(Guid.NewGuid(), sourcePartition,
        [new UpsertEdge(GraphPathRf3Scenario.Graph, EdgeId, source, seed.Target,
            GraphPathRf3Scenario.Label)]);
        var sourceReceipt = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(sourceCommand,
            cancellationToken)).ConfigureAwait(false);
        await Assert.That(sourceReceipt.Mutations.Single().Revision).IsEqualTo(1L);
        await CommitDeliveryAsync(admin, sourcePartition, seed.Target, 1, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CommitDeliveryAsync(KeyLoadClient admin, PartitionRef sourcePartition,
        EntityRef target, long revision, CancellationToken cancellationToken)
    {
        var apply = new CommandRequest(Guid.NewGuid(), target.Partition,
            [new ApplyCrossPartitionReverseEdge(sourcePartition, GraphPathRf3Scenario.Graph,
                EdgeId, target, revision)]);
        _ = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(apply, cancellationToken))
            .ConfigureAwait(false);
        var complete = new CommandRequest(Guid.NewGuid(), sourcePartition,
            [new CompleteCrossPartitionReverseEdge(sourcePartition, GraphPathRf3Scenario.Graph,
                EdgeId, target, revision)]);
        _ = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(complete, cancellationToken))
            .ConfigureAwait(false);
    }

    private static async Task AssertDeliveredRowAsync(GraphIncomingEdgesPageV1 page,
        EntityRef target, EntityRef source)
    {
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.Projection).IsEqualTo(Projection);
        var row = page.Rows.Single(candidate => candidate.Edge.Id == EdgeId
            && candidate.Edge.From == source);
        await Assert.That(row.Edge.To).IsEqualTo(target);
        await Assert.That(row.Edge.Label).IsEqualTo(GraphPathRf3Scenario.Label);
        await Assert.That(row.Edge.Revision).IsEqualTo(1L);
        await Assert.That(row.DeliveredRevision).IsEqualTo(1L);
    }

    private async Task VerifyOfficialMcpAsync(GraphPathRf3Seed seed, EntityRef source,
        CancellationToken cancellationToken)
    {
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            seed.Reader.Secret, cancellationToken);
        var reply = await mcp.CallAsync(GraphIncomingRf3Scenario.Tool, Request(seed), cancellationToken);
        var result = await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPageV1>(reply);
        await AssertDeliveredRowAsync(result.Value, seed.Target, source);
        await GraphIncomingRf3Assertions.AssertNoPrivatePayloadAsync(reply, seed.Reader.Secret);
    }
}
