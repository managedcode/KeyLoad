using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PartitionRecordCancellationStateAssertions
{
    internal static async Task VerifyHealthyAsync(ZoneTreeStore store, PartitionRef partition, string family,
        (byte[] Key, byte[] Value)[] expected, long position, long maximumBytes)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        var page = PartitionRecordNativeFixture.Read(store, partition, family, expected.Length,
            maximumBytes, maximumBytes);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Continuation).IsNull();
        await Assert.That(page.Records.Length).IsEqualTo(expected.Length);
        await Assert.That(page.Records.Zip(expected).All(pair =>
            pair.First.Key.Span.SequenceEqual(pair.Second.Key)
            && pair.First.Value.Span.SequenceEqual(pair.Second.Value))).IsTrue();
        var bytes = expected.Sum(row => (long)row.Key.Length + row.Value.Length);
        await Assert.That(page.ExaminedBytes).IsEqualTo(bytes);
        await Assert.That(page.RetainedBytes).IsEqualTo(bytes);
        await Assert.That(store.Position).IsEqualTo(position);
    }
}
