using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
namespace KeyLoad.UnitTests.Features.Search;

internal static class SharedSearchReadBudgetFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture.Configure(Collection, ResourceKind.Collection);
                fixture.Commit(new PutDocument(Collection, Id, Json),
                    new PutVector(Collection, Id, VectorField, [1, 0], Space, InitialRevision));
                var image = TenantAdmissionAssertions.State(fixture.Store);
                var position = fixture.Store.Position;
                var token = TestContext.Current!.Execution.CancellationToken;
                await RequireAsync(fixture, fixture.Store, token);
                await SearchReadBudgetCold.RunAsync(fixture, image, position,
                    cold => RequireAsync(fixture, cold, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RequireAsync(TestDatabase fixture, ZoneTreeStore store, CancellationToken token)
    {
        var image = TenantAdmissionAssertions.State(store);
        var position = store.Position;
        var identity = System.Text.Json.JsonSerializer.Serialize(store.Identity, JsonDefaults.Options);
        var documentBytes = SearchReadBudgetCost.Rows(store, KeySpace.Partition(DocumentSpace, fixture.Partition, Collection));
        var vectorBytes = SearchReadBudgetCost.Rows(store, KeySpace.Partition(VectorSpace, fixture.Partition, Collection, VectorField));
        var lineageBytes = SearchReadBudgetCost.Point(store,
            KeySpace.Partition(LineageSpace, fixture.Partition, Collection, VectorField, Id));
        var metadataBytes = SearchReadBudgetCost.TextMetadata(store, fixture.Partition, Collection);
        var maximum = checked(TwoDocumentReads * documentBytes + vectorBytes + lineageBytes + metadataBytes);
        var owner = SearchReadBudgetOwner.Create(fixture, store, maximum);
        var search = new SearchEngine(owner, UnitExecutionOptions.QueryExecution());
        var text = new SearchRequest(fixture.Partition, Collection, TextField, "alpha");
        var vector = new SearchRequest(fixture.Partition, Collection, VectorField: VectorField, Vector: [1, 0], Space: Space);
        await SearchReadBudgetAssertions.HitAsync(search, text, Json, token);
        await SearchReadBudgetAssertions.HitAsync(search, vector, Json, token);
        await SearchReadBudgetAssertions.RefusedAsync(search,
            new(fixture.Partition, Collection, TextField, "alpha", VectorField, [1, 0], Space), token);
        await Assert.That(owner.QueryReadsInFlight).IsEqualTo(NoQueries);
        await SearchReadBudgetAssertions.CancelledAsync(search, text);
        await Assert.That(owner.QueryReadsInFlight).IsEqualTo(NoQueries);
        await SearchReadBudgetAssertions.HitAsync(search, text, Json, token);
        await SearchReadBudgetAssertions.HitAsync(search, vector, Json, token);
        await Assert.That(owner.QueryReadsInFlight).IsEqualTo(NoQueries);
        await SearchReadBudgetAssertions.UnchangedAsync(store, image, position, identity);
    }
    private const int TwoDocumentReads = 2;
    private const int NoQueries = 0;
    private const int InitialRevision = 1;
    private const string Collection = "orders";
    private const string Id = "a";
    private const string Json = "{\"text\":\"alpha alpha\"}";
    private const string TextField = "/text";
    private const string VectorField = "/embedding";
    private const string DocumentSpace = "document";
    private const string VectorSpace = "vector";
    private const string LineageSpace = "vector-projection-lineage";
    private static VectorSpace Space { get; } = new("test", 2, DistanceMetric.Cosine, "test", "1");
}
