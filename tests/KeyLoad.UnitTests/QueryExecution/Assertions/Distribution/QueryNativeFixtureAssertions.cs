using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryNativeFixtureAssertions
{
    private const int MaximumFixtureRecords = 4096;

    internal static Task RunAsync(Func<QueryNativeFixture, Task> operation) => QueryNativeFixture.RunAsync(operation);

    internal static async Task ReceiptAsync(QueryNativeFixture fixture)
    {
        var expected = new CommitReceipt(QueryNativeFixture.WriteId,
            new(fixture.Destination.Store.Identity.Incarnation, QueryNativeFixture.Partition.AtomicPartitionId, fixture.WriteSequence),
            [new("putDocument", QueryNativeFixture.Collection, QueryNativeFixture.DocumentId, QueryNativeFixture.First)],
            DurabilityProfile.ProcessDurable, 1);
        await Assert.That(NativeSerialization.Serialize(fixture.Receipt).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }

    internal static string[] Image(DocumentPatchAtomicityOwner owner) => owner.Store.Read(view =>
    {
        var page = view.Scan([], MaximumFixtureRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The native query fixture exceeded its complete image bound.");
        }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static async Task StateAsync(QueryNativeFixture fixture, string[] source, string[] destination,
        long sourcePosition, long destinationPosition)
    {
        await Assert.That(Image(fixture.Source).SequenceEqual(source)).IsTrue();
        await Assert.That(Image(fixture.Destination).SequenceEqual(destination)).IsTrue();
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(destinationPosition);
    }

}
