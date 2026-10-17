using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AnalyticalReadBudgetFailureFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase(new()
            {
                MaxConcurrentQueries = OneQuery
            });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture.Configure(Collection, ResourceKind.Collection);
                fixture.Configure(EmptyCollection, ResourceKind.Collection);
                fixture.Commit(new PutDocument(Collection, "hit", Json));
                var image = TenantAdmissionAssertions.State(fixture.Store);
                var position = fixture.Store.Position;
                await RequireAsync(fixture, fixture.Store);
                await SearchReadBudgetCold.RunAsync(fixture, image, position,
                    cold => RequireAsync(fixture, cold), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RequireAsync(TestDatabase fixture, ZoneTreeStore store)
    {
        var image = TenantAdmissionAssertions.State(store);
        var position = store.Position;
        var identity = System.Text.Json.JsonSerializer.Serialize(store.Identity, JsonDefaults.Options);
        var maximum = SearchReadBudgetCost.TextMetadata(store, fixture.Partition, EmptyCollection);
        var owner = SearchReadBudgetOwner.Create(fixture, store, maximum);
        var search = new SearchEngine(owner, UnitExecutionOptions.QueryExecution());
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => search.Search(Principal,
            new(fixture.Partition, Collection, Field, Term)));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(rejected.Message).IsEqualTo(ReadBytesExceeded);
        await Assert.That(owner.QueryReadsInFlight).IsEqualTo(NoQueries);
        var healthy = new SearchRequest(fixture.Partition, EmptyCollection, Field, Term);
        await Assert.That(await search.SearchAsync(Principal, healthy)).IsEmpty();
        await Assert.That(owner.QueryReadsInFlight).IsEqualTo(NoQueries);
        await SearchReadBudgetAssertions.CancelledAsync(search, healthy);
        await Assert.That(owner.QueryReadsInFlight).IsEqualTo(NoQueries);
        await Assert.That(await search.SearchAsync(Principal, healthy)).IsEmpty();
        await SearchReadBudgetAssertions.UnchangedAsync(store, image, position, identity);
    }
    private const int OneQuery = 1;
    private const int NoQueries = 0;
    private const string Principal = "root";
    private const string Collection = "orders";
    private const string EmptyCollection = "empty";
    private const string Field = "/text";
    private const string Term = "needle";
    private const string Json = "{\"text\":\"needle\"}";
    private const string ReadBytesExceeded = "The read execution byte budget is exceeded.";
}
