namespace KeyLoad.UnitTests;

internal sealed record ReplicaSubmissionOutcomeState(long Position, long Applied, long LastIndex,
    long CommittedIndex, long Term, byte[] Rows)
{
    private const int MaximumRows = 512;

    internal static ReplicaSubmissionOutcomeState Read(TestDatabase database)
    {
        var journal = database.ReadJournalCut();
        var rows = database.Store.Read(view =>
        {
            var page = view.Scan([], MaximumRows, null);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The complete genuine fixture exceeded its bounded inventory.");
            }
            return NativeSerialization.Serialize(page);
        });
        return new(database.Store.Position, database.Database.LastApplied, journal.LastIndex,
            journal.CommittedIndex, journal.Term, rows);
    }

    internal async Task RequireAsync(ReplicaSubmissionOutcomeState actual)
    {
        await Assert.That(actual.Position).IsEqualTo(Position);
        await Assert.That(actual.Applied).IsEqualTo(Applied);
        await Assert.That(actual.LastIndex).IsEqualTo(LastIndex);
        await Assert.That(actual.CommittedIndex).IsEqualTo(CommittedIndex);
        await Assert.That(actual.Term).IsEqualTo(Term);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(Rows, actual.Rows);
    }
}
