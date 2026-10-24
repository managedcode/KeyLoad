using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.ResourceExecution;

internal static class AnalyticalReadBudgetFailureFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase(new()
            {
                MaxConcurrentQueries = 1
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
        var metadata = SearchReadBudgetCost.TextMetadata(store, fixture.Partition, EmptyCollection);
        var healthy = new SearchRequest(fixture.Partition, EmptyCollection, Field, Term);
        var measured = new SearchEngine(SearchReadBudgetOwner.Create(fixture, store,
            fixture.Database.Limits.MaxQueryReadBytes), UnitExecutionOptions.QueryExecution());
        var beforeRead = store.GetReadDiagnostics();
        await Assert.That(await measured.SearchAsync(Principal, healthy)).IsEmpty();
        var afterRead = store.GetReadDiagnostics();
        var maximum = checked(afterRead.PointExaminedBytes - beforeRead.PointExaminedBytes
            + afterRead.RangeExaminedBytes - beforeRead.RangeExaminedBytes);
        await Assert.That(maximum).IsGreaterThanOrEqualTo(metadata);
        var owner = SearchReadBudgetOwner.Create(fixture, store, maximum);
        var search = new SearchEngine(owner, UnitExecutionOptions.QueryExecution());
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => search.Search(Principal,
            new(fixture.Partition, Collection, Field, Term)));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(rejected.Message).IsEqualTo(ReadBytesExceeded);
        await Assert.That(owner.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(await search.SearchAsync(Principal, healthy)).IsEmpty();
        await Assert.That(owner.QueryReadsInFlight).IsEqualTo(0);
        await SearchReadBudgetAssertions.CancelledAsync(search, healthy);
        await Assert.That(owner.QueryReadsInFlight).IsEqualTo(0);
        await Assert.That(await search.SearchAsync(Principal, healthy)).IsEmpty();
        await SearchReadBudgetAssertions.UnchangedAsync(store, image, position, identity);
    }
    private const string Principal = "root";
    private const string Collection = "orders";
    private const string EmptyCollection = "empty";
    private const string Field = "/text";
    private const string Term = "needle";
    private const string Json = "{\"text\":\"needle\"}";
    private const string ReadBytesExceeded = "The read execution byte budget is exceeded.";
}
