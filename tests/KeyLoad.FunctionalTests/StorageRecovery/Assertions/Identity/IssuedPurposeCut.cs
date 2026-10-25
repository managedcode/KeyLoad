using KeyLoad.FunctionalTests.OrleansExecution;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record IssuedPurposeCut(long Position, string[] Rows)
{
    internal static IssuedPurposeCut Read(ConnectionServerDatabase fixture)
        => fixture.Store.Read(view =>
        {
            var page = view.Scan([], view.MaximumRangeRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The complete original execution cut exceeded its bound.");
            }
            return new IssuedPurposeCut(fixture.Store.Position, page.Records.Select(static row =>
                Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray());
        });

    internal static async Task RequireAsync(IssuedPurposeCut expected, ConnectionServerDatabase fixture)
    {
        var actual = Read(fixture);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Rows, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
