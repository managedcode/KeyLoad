using KeyLoad.Query;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class CursorExpiryBoundaryAssertions
{
    private const string Reader = "reader";
    private const string FirstId = "a";
    private const string SecondId = "b";
    private const string Json = "{}";
    private const string Scan = "bounded-full-scan";
    private const string Star = "*";
    private const string MetadataId = "/@id";
    private const int Limit = 1;
    private const long Revision = 1;
    private const long Tick = 1;
    private const string PutKind = "putDocument";
    private const string ExpiredDetail = "The query cursor no longer has a valid authorized read cut.";

    internal static async Task<string> RunAsync(TestDatabase db, QueryEngine queries, QueryRequest request,
        bool ast, ControlledReadClock clock, TimeSpan lifetime, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        var first = Read(queries, request, ast, token);
        await RowAsync(first, FirstId, cut);
        await Assert.That(first.Cursor).IsNotNull();
        var next = request with
        {
            Cursor = first.Cursor
        };
        clock.Advance(lifetime - TimeSpan.FromTicks(Tick));
        var beforeExpiry = Read(queries, next, ast, token);
        await RowAsync(beforeExpiry, SecondId, cut);
        await Assert.That(beforeExpiry.Cursor).IsNull();
        await TeamRowPolicyAssertions.UnchangedAsync(db.Store, image, cut);
        clock.Advance(TimeSpan.FromTicks(Tick));
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => Read(queries, next, ast, token));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.CursorExpired);
        await Assert.That(refused.Message).IsEqualTo(ExpiredDetail);
        await TeamRowPolicyAssertions.UnchangedAsync(db.Store, image, cut);
        var fresh = Read(queries, request, ast, token);
        await RowAsync(fresh, FirstId, cut);
        await Assert.That(fresh.Cursor).IsNotNull();
        var healthy = Read(queries, request with
        {
            Cursor = fresh.Cursor
        }, ast, token);
        await RowAsync(healthy, SecondId, cut);
        await Assert.That(healthy.Cursor).IsNull();
        await TeamRowPolicyAssertions.UnchangedAsync(db.Store, image, cut);
        return first.Cursor!;
    }

    internal static async Task ReceiptAsync(TestDatabase db, CommandRequest command, CommitReceipt receipt, long seedCut, CommitToken expectedToken)
    {
        var expected = new CommitReceipt(command.CommandId, expectedToken, [new(PutKind, "orders", FirstId, Revision), new(PutKind, "orders", SecondId, Revision)], DurabilityProfile.ProcessDurable, 1);
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(db.Store.Position).IsEqualTo(seedCut + Revision);
    }

    private static QueryPage Read(QueryEngine queries, QueryRequest request, bool ast, CancellationToken token)
        => ast ? queries.ExecuteAst(Reader, new(request.Partition,
            new SelectQuery("orders", null, [new(Star, Star)], null, [new(MetadataId, false)], Limit),
            AllowFullScan: true, Cursor: request.Cursor), cancellationToken: token)
            : queries.Execute(Reader, request, cancellationToken: token);

    private static async Task RowAsync(QueryPage page, string id, long cut)
    {
        QueryRow[] expected = [new(id, Revision, Json, false, [])];
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
    }
}
