
namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class VisibleReadTests
{
    private const string ReaderPrincipal = "alice";
    private const string Collection = "orders";
    private const string VectorField = "/embedding";
    private static readonly VectorSpace Space = new("visible-read", 2, DistanceMetric.Cosine, "test", "1");

    [Test]
    public async Task AcMp004VisitorsPreservePersistedVisibilityAndStaleVectorExclusion()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "a", "{}", Access: new(ReaderPrincipal)),
            new PutDocument(Collection, "b", "{}", Access: new(ReaderPrincipal)),
            new PutDocument(Collection, "c", "{}", Access: new("bob")),
            new PutVector(Collection, "a", VectorField, [1, 0], Space, 1),
            new PutVector(Collection, "b", VectorField, [1, 0], Space, 1),
            new PutVector(Collection, "c", VectorField, [1, 0], Space, 1));
        db.Commit(new PutDocument(Collection, "a", "{\"updated\":true}", ExpectedRevision: 1));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(ReaderPrincipal, "tenant",
            [new("database", Collection, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)], [])
        {
            OwnerId = ReaderPrincipal,
            RestrictRows = true
        })).Get<PrincipalRecord>();
        var documents = new List<string>();
        var vectors = new List<string>();
        db.Database.WithQueryView(ReaderPrincipal, db.Partition, Collection, (view, principal, _) =>
        {
            var budget = new ReadExecutionBudget(cancellationToken: TestContext.Current!.Execution.CancellationToken);
            db.Database.VisitVisibleDocuments(view, principal, db.Partition, Collection, budget,
                document => documents.Add(document.Reference.Id));
            db.Database.VisitVisibleVectors(view, principal, db.Partition, Collection, VectorField, budget,
                (document, _) => vectors.Add(document.Reference.Id));
            return documents.Count;
        });
        await Assert.That(documents).IsEquivalentTo(new[] { "a", "b" });
        await Assert.That(vectors).IsEquivalentTo(new[] { "b" });
    }
}
