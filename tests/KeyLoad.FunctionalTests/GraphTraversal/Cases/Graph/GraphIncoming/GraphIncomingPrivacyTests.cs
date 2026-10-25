namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphIncomingPrivacyTests
{
    private const string MachineKeyHiddenTarget = "hidden-target";
    private const string MachineKeyPrivateSource = "private-source";
    private const string MachineKeyReaderTarget = "reader-target";
    private const string MachineKeyVisibleSource = "visible-source";
    private const string Writer = "row-writer";
    private const string Reader = "incoming-reader";
    private const string EdgeId = "private-edge";
    private const string Label = "links";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string VertexUnavailable = "The graph vertex is unavailable.";

    [Test]
    public async Task HiddenSourceEndpointIsOmittedFromIncomingProjection()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var sourcePartition = GraphCrossPartitionTestSupport.Partition(MachineKeyPrivateSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyReaderTarget);
        var source = database.ExpectedReference(GraphCrossPartitionTestSupport.Vertex(sourcePartition, "source"));
        var target = database.ExpectedReference(GraphCrossPartitionTestSupport.Vertex(targetPartition, "target"));
        GraphCrossPartitionTestSupport.Commit(database, sourcePartition,
            new PutDocument(source.Collection, source.Id, GraphCrossPartitionTestSupport.EmptyJson,
                Access: new RowAccess(Writer)));
        GraphCrossPartitionTestSupport.Commit(database, targetPartition,
            new PutDocument(target.Collection, target.Id, GraphCrossPartitionTestSupport.EmptyJson,
                Access: new RowAccess(Reader)));
        GraphCrossPartitionTestSupport.Commit(database, sourcePartition,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, source, target, Label));
        GraphCrossPartitionTestSupport.Deliver(database, sourcePartition, target, EdgeId, 1);
        var principal = GraphCrossPartitionTestSupport.CreateRowRestrictedReader(database, Reader);

        var page = GraphCrossPartitionTestSupport.ReadIncoming(database, target, principal: Reader);

        await Assert.That(page.Rows.IsEmpty).IsTrue();
        await GraphIncomingRowAuthorityContinuation.RunAsync(database, source, target, principal, repairTarget: false);
    }

    [Test]
    public async Task HiddenTargetEndpointIsNotFoundWithoutReturningProjectionRows()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var sourcePartition = GraphCrossPartitionTestSupport.Partition(MachineKeyVisibleSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyHiddenTarget);
        var source = database.ExpectedReference(GraphCrossPartitionTestSupport.Vertex(sourcePartition, "source"));
        var target = database.ExpectedReference(GraphCrossPartitionTestSupport.Vertex(targetPartition, "target"));
        GraphCrossPartitionTestSupport.SeedVertices(database, source);
        GraphCrossPartitionTestSupport.Commit(database, targetPartition,
            new PutDocument(target.Collection, target.Id, GraphCrossPartitionTestSupport.EmptyJson,
                Access: new RowAccess(Writer)));
        GraphCrossPartitionTestSupport.Commit(database, sourcePartition,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, source, target, Label));
        GraphCrossPartitionTestSupport.Deliver(database, sourcePartition, target, EdgeId, 1);
        var principal = GraphCrossPartitionTestSupport.CreateRowRestrictedReader(database, Reader);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            GraphCrossPartitionTestSupport.ReadIncoming(database, target, principal: Reader));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.NotFound);
        await GraphIncomingRowAuthorityContinuation.RunAsync(database, source, target, principal, repairTarget: true);
    }

    [Test]
    public async Task TargetCollectionReadDenialIsNotFoundUntilTheGrantIsRepaired()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var sourcePartition = GraphCrossPartitionTestSupport.Partition(MachineKeyVisibleSource);
        var source = database.ExpectedReference(GraphCrossPartitionTestSupport.Vertex(sourcePartition, "source"));
        var target = database.ExpectedReference(GraphCrossPartitionTestSupport.Vertex(
            GraphCrossPartitionTestSupport.Partition(MachineKeyReaderTarget), "target"));
        GraphCrossPartitionTestSupport.SeedVertices(database, source, target);
        GraphCrossPartitionTestSupport.Commit(database, sourcePartition,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, source, target, Label));
        GraphCrossPartitionTestSupport.Deliver(database, sourcePartition, target, EdgeId, 1);
        var graphOnly = new ScopeGrant(Database, GraphCrossPartitionTestSupport.Graph, Capability.GraphRead);
        await ConfigureAsync(database, new PrincipalRecord(Reader, Tenant, [graphOnly], []) { PolicyEpoch = 1 });
        var image = CanonicalStoreImage.Bytes(database.Store);
        var cut = database.Store.Position;

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            GraphCrossPartitionTestSupport.ReadIncoming(database, target, principal: Reader));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(failure.Message).IsEqualTo(VertexUnavailable);
        await Assert.That(CanonicalStoreImage.Bytes(database.Store)).IsEquivalentTo(image);
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        var documents = new ScopeGrant(Database, GraphCrossPartitionTestSupport.Collection, Capability.DocumentsRead);
        await ConfigureAsync(database, new PrincipalRecord(Reader, Tenant, [graphOnly, documents], []) { PolicyEpoch = 2 });
        var page = GraphCrossPartitionTestSupport.ReadIncoming(database, target, principal: Reader);
        await Assert.That(page.Rows.Length).IsEqualTo(1);
        await Assert.That(page.Rows[0].Edge.From).IsEqualTo(source);
        await Assert.That(page.Rows[0].DeliveredRevision).IsEqualTo(1L);
    }

    private static async Task ConfigureAsync(TestDatabase database, PrincipalRecord principal)
    {
        var actual = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(principal, JsonDefaults.Options));
    }
}
