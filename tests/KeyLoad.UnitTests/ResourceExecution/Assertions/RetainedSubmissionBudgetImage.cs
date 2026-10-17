using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record RetainedSubmissionBudgetImage(Dictionary<string, string> Rows, long Position,
    (long LastIndex, long CommittedIndex, long Term) Journal)
{
    private const int MaximumImageRows = 64;
    private const string BoundedImageRequired = "The retained-outcome fixture exceeded its bounded image.";

    internal static RetainedSubmissionBudgetImage Capture(TestDatabase fixture)
        => new(RowsOf(fixture), fixture.Store.Position, fixture.ReadJournalCut());

    internal async Task UnchangedAsync(TestDatabase fixture)
    {
        var actual = RowsOf(fixture);
        await Assert.That(actual.Count).IsEqualTo(Rows.Count);
        foreach (var row in Rows)
        {
            await Assert.That(actual.ContainsKey(row.Key)).IsTrue();
            await Assert.That(actual[row.Key]).IsEqualTo(row.Value);
        }
        await Assert.That(fixture.Store.Position).IsEqualTo(Position);
        await Assert.That(fixture.ReadJournalCut()).IsEqualTo(Journal);
    }

    internal static async Task ResultAsync(OperationResult expected, OperationResult? actual)
    {
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Error).IsEqualTo(expected.Error);
        await Assert.That(actual.SafeDetail).IsEqualTo(expected.SafeDetail);
        await Assert.That(actual.Json).IsEqualTo(expected.Json);
        await Assert.That(JsonSerializer.Serialize(actual.Get<CommitReceipt>(), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected.Get<CommitReceipt>(), JsonDefaults.Options));
    }

    private static Dictionary<string, string> RowsOf(TestDatabase fixture) => fixture.Store.Read(view =>
    {
        var page = view.Scan([], MaximumImageRows);
        if (page.HasMore)
        {
            throw new InvalidOperationException(BoundedImageRequired);
        }

        return page.Records.ToDictionary(row => Convert.ToHexString(row.Key.Span), row => Convert.ToHexString(row.Value.Span));
    });
}
