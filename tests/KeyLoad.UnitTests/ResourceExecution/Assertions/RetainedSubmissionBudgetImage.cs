using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record RetainedSubmissionBudgetImage(Dictionary<string, string> Rows, long Position,
    (long AppliedPosition, long StorePosition) Cut)
{
    private const int MaximumImageRows = 64;
    private const string BoundedImageRequired = "The retained-outcome fixture exceeded its bounded image.";

    internal static RetainedSubmissionBudgetImage Capture(TestDatabase fixture)
        => new(RowsOf(fixture), fixture.Store.Position, fixture.ReadCanonicalCut());

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
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(Cut);
    }

    internal static async Task ResultAsync(Result<object> expected, Result<object>? actual)
    {
        await Assert.That(actual).IsNotNull();
        await NativeReplayResultAssertions.Same<CommitReceipt>(actual!, expected);
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
