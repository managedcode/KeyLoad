using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class VisibleReadTests
{
    private const string RootPrincipal = "root";
    private const string ReaderPrincipal = "alice";
    private const string Collection = "orders";
    private const string VectorField = "/embedding";
    private static readonly VectorSpace Space = new("visible-read", 2, DistanceMetric.Cosine, "test", "1");

    [Test]
    public async Task AcMp003VisibleDocumentsAreVisitedBeforeSubsequentByteExhaustion()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "a", "{}"), new PutDocument(Collection, "b", "{}"));
        var acceptedBytes = db.Store.Read(view => view.Scan(DocumentStorageKeys.Prefix(db.Partition, Collection), 1)
            .Records.Sum(record => (long)record.Key.Length + record.Value.Length));
        var limited = new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxQueryReadBytes = acceptedBytes }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limited.Limits), cancellationToken: TestContext.Current!.Execution.CancellationToken);
        var visited = new List<string>();
        var position = db.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => limited.WithQueryView(RootPrincipal,
            db.Partition, Collection, (view, principal, _) =>
            {
                limited.VisitVisibleDocuments(view, principal, db.Partition, Collection, budget,
                    document => visited.Add(document.Reference.Id));
                return visited.Count;
            }));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(visited).IsEquivalentTo(new[] { "a" });
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(db.Database.WithDocuments(RootPrincipal, db.Partition, Collection,
            (_, _, documents) => documents.Length)).IsEqualTo(2);
    }

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
        { OwnerId = ReaderPrincipal, RestrictRows = true })).Get<PrincipalRecord>();
        var documents = new List<string>();
        var vectors = new List<string>();
        db.Database.WithQueryView(ReaderPrincipal, db.Partition, Collection, (view, principal, _) =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(db.Database.Limits), cancellationToken: TestContext.Current!.Execution.CancellationToken);
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
