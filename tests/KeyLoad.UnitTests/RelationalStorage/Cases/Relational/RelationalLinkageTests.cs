using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal sealed class RelationalLinkageTests
{
    private const string Graph = "links";
    private const string Edge = "first-second";
    private const string Label = "related";
    private const string VectorField = "/embedding";
    private const string VectorSpaceId = "typed-space";
    private const string Model = "model";
    private const string Version = "1";
    private const string Alpha = "alpha";
    private const string Beta = "beta";
    private const string Reader = "reader";
    private const string ProtectedClassification = "private";
    private const string ChangedName = "\"changed\"";

    [Test]
    public async Task AcAisql004TypedRowsRemainGraphEndpointsAndVectorSubjects()
    {
        using var database = new TestDatabase();
        RelationalTestData.Configure(database);
        database.Configure(Graph, ResourceKind.Graph);
        var first = new EntityRef(database.Partition, RelationalTestData.Table, RelationalTestData.First);
        var second = new EntityRef(database.Partition, RelationalTestData.Table, RelationalTestData.Second);
        var space = new VectorSpace(VectorSpaceId, 2, DistanceMetric.DotProduct, Model, Version);
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.First, RelationalTestData.Row()),
            new PutDocument(RelationalTestData.Table, RelationalTestData.Second, RelationalTestData.Row(RelationalTestData.Second).Replace(Alpha, Beta, StringComparison.Ordinal)),
            new UpsertEdge(Graph, Edge, first, second, Label), new PutVector(RelationalTestData.Table, RelationalTestData.First, VectorField, [1, 0], space, 1));

        var graph = database.Database.Traverse(RelationalTestData.Root, database.Partition, Graph, first,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        await Assert.That(graph.Vertices).IsEquivalentTo(new[] { first, second }, CollectionOrdering.Matching);
        await Assert.That(graph.Edges).HasSingleItem();
        var ranked = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(RelationalTestData.Root,
            new(database.Partition, RelationalTestData.Table, VectorField: VectorField, Vector: [1, 0], Space: space),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(ranked).HasSingleItem();
        await Assert.That(ranked[0].Document.Reference).IsEqualTo(first);
    }

    [Test]
    public async Task AcAisql004TypedRowSchemaCannotBypassExistingProtectedWriteAuthority()
    {
        using var database = new TestDatabase();
        var definition = RelationalTestData.Definition() with
        {
            FieldPolicies = [new(RelationalTestData.NamePath, ProtectedClassification)]
        };
        RelationalTestData.Configure(database, definition);
        database.Commit(new PutDocument(RelationalTestData.Table, RelationalTestData.First, RelationalTestData.Row()));
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, RelationalTestData.Table, Capability.DocumentsRead | Capability.DocumentsWrite)], []))).Get<PrincipalRecord>();
        var id = Guid.NewGuid();
        var denied = database.Submit(OperationKind.Batch, new CommandRequest(id, database.Partition,
            [new PatchDocument(RelationalTestData.Table, RelationalTestData.First, [new(RelationalTestData.NamePath, PatchKind.Set, ChangedName)], 1)]), Reader, id);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(database.Database.GetDocument(RelationalTestData.Root,
            new(database.Partition, RelationalTestData.Table, RelationalTestData.First))!.Revision).IsEqualTo(1);
    }
}
