using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Authorization;

internal static class PolicyEpochCursorAssertions
{
    private const string Reader = "reader";
    private const string Scan = "bounded-full-scan";
    private const string FirstRow = "a";
    private const string LastRow = "b";
    private const string OriginalJson = "{}";
    private const long OriginalRevision = 1;

    internal static async Task RefusedAsync(QueryEngine queries, ZoneTreeStore store, QueryRequest request,
        string cursor, ErrorCode expected, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(Reader,
            request with
            {
                Cursor = cursor
            }, cancellationToken: token));
        await Assert.That(error.Code).IsEqualTo(expected);
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    internal static async Task<string> PagesAsync(QueryEngine queries, ZoneTreeStore store, QueryRequest request, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var first = queries.Execute(Reader, request, cancellationToken: token);
        await RowAsync(first, FirstRow, cut);
        await Assert.That(first.Cursor).IsNotNull();
        var second = queries.Execute(Reader, request with
        {
            Cursor = first.Cursor
        }, cancellationToken: token);
        await RowAsync(second, LastRow, cut);
        await Assert.That(second.Cursor).IsNull();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
        return first.Cursor!;
    }

    internal static async Task ContinuationAsync(QueryEngine queries, ZoneTreeStore store, QueryRequest request,
        string cursor, long cut, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var page = queries.Execute(Reader, request with
        {
            Cursor = cursor
        }, cancellationToken: token);
        await RowAsync(page, LastRow, cut);
        await Assert.That(page.Cursor).IsNull();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    private static async Task RowAsync(QueryPage page, string id, long cut)
    {
        QueryRow[] expected = [new(id, OriginalRevision, OriginalJson, false, [])];
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
    }
}
