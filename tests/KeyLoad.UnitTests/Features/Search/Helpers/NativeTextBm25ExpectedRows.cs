using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextBm25ExpectedRows
{
    private const string Collection = "orders";
    internal static DocumentResult Document(PartitionRef partition, string id, long revision,
        string json, bool redacted, ImmutableArray<string> fields)
    {
        var owned = new PartitionRef(Copy(partition.TenantId), Copy(partition.DatabaseId),
            Copy(partition.TransactionDomainId), Copy(partition.PartitionKey));
        return new(new(owned, Copy(Collection), Copy(id)), revision, Copy(json), redacted, fields);
    }

    private static string Copy(string literal) => new(literal.AsSpan());

    internal static async Task RequireOwnershipAsync(RankedDocument[] expected, RankedDocument[] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await SeparateAsync(expected[index].Document.Reference);
            await SeparateAsync(actual[index].Document.Reference);
            if (index > 0)
            {
                await Assert.That(ReferenceEquals(expected[index - 1].Document.Reference.Partition,
                    expected[index].Document.Reference.Partition)).IsFalse();
                await Assert.That(ReferenceEquals(actual[index - 1].Document.Reference.Partition,
                    actual[index].Document.Reference.Partition)).IsFalse();
            }
        }
    }

    private static async Task SeparateAsync(EntityRef reference)
        => await Assert.That(ReferenceEquals(reference.Partition.TransactionDomainId, reference.Collection)).IsFalse();
}
