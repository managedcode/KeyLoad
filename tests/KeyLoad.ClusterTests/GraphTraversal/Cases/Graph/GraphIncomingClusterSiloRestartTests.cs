using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.GraphTraversal;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class GraphIncomingClusterSiloRestartTests(ClusterFixture fixture)
{
    private const string SourceCollection = GraphPathClusterScenario.CollectionA;
    private const string EdgeId = "incoming-survives-silo-rejoin";
    private const string Projection = "eventual-reverse";
    private const string GuidFormat = "N";
    private const int RequestVersion = 1;

    [Test]
    public async Task DeliveredIncomingProjectionSurvivesOwnedSiloRestartAndRejoin()
    {
        using var deadline = McpCallerDeadline.Create();
        var seed = await GraphPathClusterScenario.CreateAsync(fixture, deadline.Token);
        seed = seed with
        {
            Reader = await GraphPathClusterScenario.GrantLabelUseAsync(fixture, seed, deadline.Token)
        };
        var sourcePartition = seed.Partition with
        {
            PartitionKey = Guid.NewGuid().ToString(GuidFormat)
        };
        var source = new EntityRef(sourcePartition, SourceCollection, "remote-source");
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await SeedCrossEdgeAsync(admin, seed, sourcePartition, source, deadline.Token);
        using var clients = new GraphPathClusterNodeClients(fixture, seed.Reader.Secret);

        await GraphPathClusterSiloRestart.ExecuteAsync(fixture, seed, clients, deadline.Token);
        foreach (var reader in clients.Readers)
        {
            var page = await McpCallerAssertions.SdkSuccessAsync(await reader.IncomingEdgesAsync(
                Request(seed), deadline.Token));
            await AssertDeliveredRowAsync(page, seed.Stored(seed.Target), seed.Stored(source));
        }
        await VerifyOfficialMcpAsync(seed, source, deadline.Token);
    }

    private static ReadIncomingGraphEdgesRequest Request(GraphPathClusterSeed seed)
        => new(RequestVersion, seed.Target, GraphPathClusterScenario.Graph, 10);

    private static async Task SeedCrossEdgeAsync(KeyLoadClient admin, GraphPathClusterSeed seed,
        PartitionRef sourcePartition, EntityRef source, CancellationToken cancellationToken)
    {
        var seedCommand = new CommandRequest(Guid.NewGuid(), sourcePartition,
            [new PutDocument(SourceCollection, source.Id, GraphIncomingClusterScenario.EmptyJson,
                Access: new(seed.Reader.Principal.Id))]);
        _ = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(seedCommand, cancellationToken))
            .ConfigureAwait(false);
        var sourceCommand = new CommandRequest(Guid.NewGuid(), sourcePartition,
        [new UpsertEdge(GraphPathClusterScenario.Graph, EdgeId, source, seed.Target,
            GraphPathClusterScenario.Label)]);
        var sourceReceipt = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(sourceCommand,
            cancellationToken)).ConfigureAwait(false);
        await Assert.That(sourceReceipt.Mutations.Single().Revision).IsEqualTo(1L);
        await CommitDeliveryAsync(admin, sourcePartition, seed.Target, 1, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CommitDeliveryAsync(KeyLoadClient admin, PartitionRef sourcePartition,
        EntityRef target, long revision, CancellationToken cancellationToken)
    {
        var apply = new CommandRequest(Guid.NewGuid(), target.Partition,
            [new ApplyCrossPartitionReverseEdge(sourcePartition, GraphPathClusterScenario.Graph,
                EdgeId, target, revision)]);
        _ = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(apply, cancellationToken))
            .ConfigureAwait(false);
        var complete = new CommandRequest(Guid.NewGuid(), sourcePartition,
            [new CompleteCrossPartitionReverseEdge(sourcePartition, GraphPathClusterScenario.Graph,
                EdgeId, target, revision)]);
        _ = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(complete, cancellationToken))
            .ConfigureAwait(false);
    }

    private static async Task AssertDeliveredRowAsync(GraphIncomingEdgesPage page,
        EntityRef target, EntityRef source)
    {
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.Projection).IsEqualTo(Projection);
        var row = page.Rows.Single(candidate => candidate.Edge.Id == EdgeId
            && candidate.Edge.From == source);
        await Assert.That(row.Edge.To).IsEqualTo(target);
        await Assert.That(row.Edge.Label).IsEqualTo(GraphPathClusterScenario.Label);
        await Assert.That(row.Edge.Revision).IsEqualTo(1L);
        await Assert.That(row.DeliveredRevision).IsEqualTo(1L);
    }

    private async Task VerifyOfficialMcpAsync(GraphPathClusterSeed seed, EntityRef source,
        CancellationToken cancellationToken)
    {
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            seed.Reader.Secret, cancellationToken);
        var reply = await mcp.CallAsync(GraphIncomingClusterScenario.Tool, Request(seed), cancellationToken);
        var result = await McpCallerAssertions.SuccessAsync<GraphIncomingEdgesPage>(reply);
        await AssertDeliveredRowAsync(result.Value, seed.Stored(seed.Target), seed.Stored(source));
        await GraphIncomingClusterAssertions.AssertNoPrivatePayloadAsync(reply, seed.Reader.Secret);
    }
}
