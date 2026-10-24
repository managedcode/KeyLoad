using KeyLoad.UnitTests.DocumentStorage;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryFixtureAssertions
{
    private const int MaximumFixtureRecords = 4096;

    internal static Task RunAsync(Func<QueryFixture, Task> operation) => QueryFixture.RunAsync(operation);

    internal static async Task ReceiptAsync(QueryFixture fixture)
    {
        var expected = new CommitReceipt(QueryFixture.WriteId,
            new(fixture.Destination.Store.Identity.Incarnation, QueryFixture.Partition.AtomicPartitionId, fixture.WriteSequence),
            [new("putDocument", QueryFixture.Collection, QueryFixture.DocumentId, QueryFixture.First)],
            DurabilityProfile.ProcessDurable, 1);
        await Assert.That(OrleansSerialization.Serialize(fixture.Receipt).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
    }

    internal static string[] Image(DocumentPatchAtomicityOwner owner) => owner.Store.Read(view =>
    {
        var page = view.Scan([], MaximumFixtureRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The execution query fixture exceeded its complete image bound.");
        }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static async Task StateAsync(QueryFixture fixture, string[] source, string[] destination,
        long sourcePosition, long destinationPosition)
    {
        await Assert.That(Image(fixture.Source).SequenceEqual(source)).IsTrue();
        await Assert.That(Image(fixture.Destination).SequenceEqual(destination)).IsTrue();
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(destinationPosition);
    }

}
