using System.Globalization;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class LiveQueryPermutationOracle(TestDatabase db, QueryEngine engine, AstQueryRequest query)
{
    private const string Root = "root";
    private const string Collection = "orders";
    private const string IdentityPrefix = "order-";
    private const int RevisionAdvance = 1;
    private const long InitialHorizon = 0;
    private const int PageLimit = 3;
    private readonly Dictionary<string, QueryRow> expectedRows = new(StringComparer.Ordinal);
    private string[] originalImage = [];
    private long originalPosition;

    internal async Task InitialAsync(LiveQuerySnapshot snapshot)
    {
        await Assert.That(snapshot.Rows).IsEmpty();
        await Assert.That(snapshot.ThroughSequence).IsEqualTo(InitialHorizon);
        await Assert.That(snapshot.CutPosition).IsEqualTo(db.Store.Position);
        await Assert.That(string.IsNullOrEmpty(snapshot.Cursor)).IsFalse();
    }

    internal void Capture()
    {
        originalImage = global::KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(db.Store);
        originalPosition = db.Store.Position;
    }

    internal async Task ObserveAsync(LiveQueryPage page, LiveQueryPage replay, CommitReceipt receipt,
        int index, int trial, int round)
    {
        var id = IdentityPrefix + index.ToString(CultureInfo.InvariantCulture);
        var revision = round + RevisionAdvance;
        var body = "{\"number\":" + trial.ToString(CultureInfo.InvariantCulture) + ",\"status\":\"open\"}";
        var removed = round is LiveQueryTestSchedule.LeaveRound or LiveQueryTestSchedule.NonmatchingUpdateRound
            or LiveQueryTestSchedule.DeleteRound or LiveQueryTestSchedule.NonmatchingInsertRound;
        QueryRow? row = removed ? null : new(id, revision, body, false, []);
        if (row is null)
        {
            expectedRows.Remove(id);
        }
        else
        {
            expectedRows[id] = row;
        }
        var filtered = round is LiveQueryTestSchedule.NonmatchingUpdateRound or LiveQueryTestSchedule.NonmatchingInsertRound;
        var kind = removed ? LiveQueryChangeKind.Remove : LiveQueryChangeKind.Upsert;
        LiveQueryChange[] changes = filtered ? [] : [new(trial + RevisionAdvance, receipt.Token, kind,
            db.ExpectedReference(new(db.Partition, Collection, id)), revision, row)];
        await PageAsync(page, changes, trial + RevisionAdvance);
        await PageAsync(replay, changes, trial + RevisionAdvance);
        var empty = engine.ReadLiveQuery(Root, new(query, page.Cursor, Limit: PageLimit));
        await PageAsync(empty, [], trial + RevisionAdvance);
        await Assert.That(db.Store.Position).IsEqualTo(originalPosition);
        await Assert.That(global::KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(db.Store)
            .SequenceEqual(originalImage, StringComparer.Ordinal)).IsTrue();
    }

    internal Task MaterializedAsync(IEnumerable<QueryRow> actual) => SameAsync(expectedRows.Values, actual);

    private async Task PageAsync(LiveQueryPage actual, LiveQueryChange[] changes, long horizon)
    {
        await Assert.That(string.IsNullOrEmpty(actual.Cursor)).IsFalse();
        var expected = new LiveQueryPage([.. changes], actual.Cursor, horizon, false, originalPosition);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static void Visit(int[] visits, int index, int round)
    {
        if (round < LiveQueryTestSchedule.FullRounds)
        {
            visits[round * LiveQueryTestSchedule.IdentityCount + index]++;
        }
    }

    internal static void Apply(Dictionary<string, QueryRow> rows, LiveQueryPage page)
    {
        foreach (var change in page.Changes)
        {
            if (change.Kind == LiveQueryChangeKind.Remove)
            {
                rows.Remove(change.Reference.Id);
            }
            else
            {
                rows[change.Reference.Id] = change.Row!;
            }
        }
    }

    internal static async Task SameAsync(IEnumerable<QueryRow> expected, IEnumerable<QueryRow> actual)
        => await Assert.That(JsonDefaults.Serialize(actual.OrderBy(row => row.EntityId, StringComparer.Ordinal).ToArray())
            .SequenceEqual(JsonDefaults.Serialize(expected.OrderBy(row => row.EntityId, StringComparer.Ordinal).ToArray()))).IsTrue();
}
