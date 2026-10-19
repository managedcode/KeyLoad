using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record SignedPurposeCut(long Position, string[] Rows)
{
    internal static SignedPurposeCut Read(ConnectionServerDatabase fixture)
        => fixture.Store.Read(view =>
        {
            var page = view.Scan([], fixture.Database.Limits.MaxScanRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The complete original execution cut exceeded its bound.");
            }
            return new SignedPurposeCut(fixture.Store.Position, page.Records.Select(static row =>
                Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray());
        });

    internal static async Task RequireAsync(SignedPurposeCut expected, ConnectionServerDatabase fixture)
    {
        var actual = Read(fixture);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Rows, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
