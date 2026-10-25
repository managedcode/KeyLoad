using System.Collections.Immutable;

using System.Text.Json;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextBm25ExpectedRows
{
    private const string Collection = "orders";
    internal static DocumentResult Document(TestDatabase provenance, string id, long revision,
        string json, bool redacted, ImmutableArray<string> fields)
    {
        var partition = provenance.Partition;
        var owned = new PartitionRef(Copy(partition.TenantId), Copy(partition.DatabaseId),
            Copy(partition.TransactionDomainId), Copy(partition.PartitionKey));
        return new(provenance.ExpectedReference(new(owned, Copy(Collection), Copy(id))), revision, Copy(json), redacted, fields);
    }

    private static string Copy(string literal) => new(literal.AsSpan());

    internal static async Task RequireOwnershipAsync(RankedDocument[] expected, RankedDocument[] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await SeparateAsync(expected[index].Document.Reference);
            await Assert.That(actual[index].Document.Reference).IsEqualTo(expected[index].Document.Reference);
            if (index > 0)
            {
                await Assert.That(ReferenceEquals(expected[index - 1].Document.Reference.Partition,
                    expected[index].Document.Reference.Partition)).IsFalse();
            }
        }
    }

    private static async Task SeparateAsync(EntityRef reference)
        => await Assert.That(ReferenceEquals(reference.Partition.TransactionDomainId, reference.Collection)).IsFalse();

    internal static async Task RequireWholeValuesAsync(RankedDocument[] expected, RankedDocument[] actual)
    {
        var independentValues = JsonSerializer.Serialize(expected, JsonDefaults.Options);
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options)).IsEqualTo(independentValues);
        var bytes = OrleansSerialization.Serialize(actual);
        var reopened = OrleansSerialization.Deserialize<RankedDocument[]>(bytes);
        await Assert.That(JsonSerializer.Serialize(reopened, JsonDefaults.Options)).IsEqualTo(independentValues);
    }
}
