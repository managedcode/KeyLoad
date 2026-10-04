using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkStoreAssertions
{
    internal static async Task RecordsAsync(SampleRecord[] expected, SampleRecord[] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await RecordAsync(expected[index], actual[index]);
        }
    }

    internal static async Task RecordAsync(SampleRecord expected, SampleRecord actual)
    {
        await Assert.That(actual.SeriesId).IsEqualTo(expected.SeriesId);
        await Assert.That(actual.Sample.EventId).IsEqualTo(expected.Sample.EventId);
        await Assert.That(actual.Sample.Timestamp.UtcTicks).IsEqualTo(expected.Sample.Timestamp.UtcTicks);
        await Assert.That(actual.Sample.Timestamp.Ticks).IsEqualTo(expected.Sample.Timestamp.Ticks);
        await Assert.That(actual.Sample.Timestamp.Offset).IsEqualTo(expected.Sample.Timestamp.Offset);
        await Assert.That(BitConverter.DoubleToInt64Bits(actual.Sample.Value))
            .IsEqualTo(BitConverter.DoubleToInt64Bits(expected.Sample.Value));
        await Assert.That(actual.Sequence).IsEqualTo(expected.Sequence);
        await Assert.That(actual.TagsJson).IsEqualTo(expected.TagsJson);
    }

    internal static async Task AggregateAsync(SampleAggregate expected, SampleAggregate actual)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        await Assert.That(actual.Sum).IsEqualTo(expected.Sum);
        await Assert.That(actual.Minimum).IsEqualTo(expected.Minimum);
        await Assert.That(actual.Maximum).IsEqualTo(expected.Maximum);
        await Assert.That(actual.Average).IsEqualTo(expected.Average);
    }

    internal static async Task WindowsAsync(SampleAggregateWindow[] expected, SampleAggregateWindow[] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].From).IsEqualTo(expected[index].From);
            await Assert.That(actual[index].UntilExclusive).IsEqualTo(expected[index].UntilExclusive);
            await AggregateAsync(expected[index].Aggregate, actual[index].Aggregate);
        }
    }

    internal static async Task CanonicalStateAsync(SampleChunkStoreCanonicalState expected,
        SampleChunkStoreCanonicalState actual)
    {
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await CanonicalRecordsAsync(expected.Records, actual.Records);
    }

    internal static async Task CanonicalRecordsAsync(KeyValueRecord[] expected, KeyValueRecord[] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Key.ToArray())
                .IsEquivalentTo(expected[index].Key.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(actual[index].Value.ToArray())
                .IsEquivalentTo(expected[index].Value.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }
}
