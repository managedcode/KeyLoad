namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record NativeSignedPurposeCut(long Position, string[] Rows)
{
    internal static NativeSignedPurposeCut Read(TestDatabase fixture)
        => fixture.Store.Read(view =>
        {
            var page = view.Scan([], fixture.Database.Limits.MaxScanRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The complete original native cut exceeded its bound.");
            }
            return new NativeSignedPurposeCut(fixture.Store.Position, page.Records.Select(static row =>
                Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray());
        });

    internal static async Task RequireAsync(NativeSignedPurposeCut expected, TestDatabase fixture)
    {
        var actual = Read(fixture);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Rows, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
