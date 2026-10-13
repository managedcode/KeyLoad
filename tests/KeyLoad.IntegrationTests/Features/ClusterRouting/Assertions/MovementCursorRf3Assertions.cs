namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MovementCursorRf3Assertions
{
    private const int FirstRow = 0;
    private const int SingleRow = 1;
    private const int BothRows = 2;
    private const int NoChanges = 0;
    private const long OriginalRevision = 1;
    private const long InitialCut = 0;

    internal static async Task FirstAsync(QueryPage page)
    {
        await Assert.That(page.Rows.Length).IsEqualTo(SingleRow);
        await RowAsync(page.Rows[FirstRow], MovementCursorRf3Corpus.First, MovementCursorRf3Corpus.FirstJson);
        await Assert.That(page.AccessPath).IsEqualTo(MovementCursorRf3Corpus.IndexPath);
        await Assert.That(page.CutPosition).IsGreaterThan(InitialCut);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }

    internal static async Task SecondAsync(QueryPage page, QueryPage original)
    {
        await Assert.That(page.Rows.Length).IsEqualTo(SingleRow);
        await RowAsync(page.Rows[FirstRow], MovementCursorRf3Corpus.Second, MovementCursorRf3Corpus.SecondJson);
        await Assert.That(page.AccessPath).IsEqualTo(MovementCursorRf3Corpus.IndexPath);
        await Assert.That(page.CutPosition).IsEqualTo(original.CutPosition);
        await Assert.That(page.Cursor).IsNull();
    }

    internal static async Task LiveSnapshotAsync(LiveQuerySnapshot snapshot)
    {
        await Assert.That(snapshot.Rows.Length).IsEqualTo(BothRows);
        await RowAsync(snapshot.Rows.Single(row => row.EntityId == MovementCursorRf3Corpus.First),
            MovementCursorRf3Corpus.First, MovementCursorRf3Corpus.FirstJson);
        await RowAsync(snapshot.Rows.Single(row => row.EntityId == MovementCursorRf3Corpus.Second),
            MovementCursorRf3Corpus.Second, MovementCursorRf3Corpus.SecondJson);
        await Assert.That(string.IsNullOrEmpty(snapshot.Cursor)).IsFalse();
    }

    internal static async Task LiveEmptyAsync(LiveQueryPage page, LiveQuerySnapshot original)
    {
        await Assert.That(page.Changes.Length).IsEqualTo(NoChanges);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.ThroughSequence).IsGreaterThanOrEqualTo(original.ThroughSequence);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }

    private static async Task RowAsync(QueryRow actual, string id, string json)
    {
        await Assert.That(actual.EntityId).IsEqualTo(id);
        await Assert.That(actual.Revision).IsEqualTo(OriginalRevision);
        await Assert.That(actual.Json).IsEqualTo(json);
        await Assert.That(actual.Redacted).IsFalse();
        await Assert.That(actual.RedactedFields is null || actual.RedactedFields.Value.IsEmpty).IsTrue();
        await Assert.That(actual.Sources).IsNull();
    }
}
