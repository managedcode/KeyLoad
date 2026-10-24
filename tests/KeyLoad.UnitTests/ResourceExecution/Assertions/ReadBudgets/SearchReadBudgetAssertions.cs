using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.ResourceExecution;

internal static class SearchReadBudgetAssertions
{
    internal static async Task UnchangedAsync(ZoneTreeStore store, string[] image, long position, string identity)
    {
        await Assert.That(TenantAdmissionAssertions.State(store).SequenceEqual(image)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(store.Identity, JsonDefaults.Options)).IsEqualTo(identity);
    }
    internal static async Task HitAsync(TestDatabase provenance, SearchEngine search, SearchRequest request, string json, CancellationToken token)
    {
        var expected = new RankedDocument(new(provenance.ExpectedReference(new(request.Partition, request.Collection, "a")),
            InitialRevision, json, false, []), FirstScore);
        var actual = await search.SearchAsync(Principal, request, token);
        await Assert.That(actual).HasSingleItem();
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(new[] { expected }))).IsTrue();
    }
    internal static async Task CancelledAsync(SearchEngine search, SearchRequest request)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var original = Assert.ThrowsExactly<OperationCanceledException>(() => search.Search(Principal, request, cancellation.Token));
        await Assert.That(original.CancellationToken).IsEqualTo(cancellation.Token);
    }
    internal static async Task RefusedAsync(SearchEngine search, SearchRequest request, CancellationToken token)
    {
        var original = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => search.SearchAsync(Principal, request, token)))!;
        await Assert.That(original.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(original.Message).IsEqualTo(ReadBytesExceeded);
    }
    internal const string Principal = "root";
    private const long InitialRevision = 1;
    private const double FirstScore = 1d / 61d;
    private const string ReadBytesExceeded = "The read execution byte budget is exceeded.";
}
