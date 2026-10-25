using KeyLoad.Core;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal static class EventAppendIndexAssertions
{
    private const int MaximumEntries = 3;
    private const string IndexSpace = "index";
    private const string Separator = ":";

    internal static async Task VerifyAsync(IAtomicStore store, bool committed, bool healthy)
    {
        var identities = new List<string>();
        if (committed)
        {
            identities.Add(EventAppendCrashContract.Producer);
        }
        if (healthy)
        {
            identities.Add(EventAppendCrashContract.Healthy);
        }
        var actual = store.Read(view => view.Scan(KeySpace.Partition(IndexSpace,
            EventAppendCrashContract.Partition, EventAppendCrashContract.Documents), MaximumEntries));
        await Assert.That(actual.HasMore).IsFalse();
        var expected = identities.Select(id => Convert.ToHexString(KeySpace.Partition(IndexSpace,
            EventAppendCrashContract.Partition, EventAppendCrashContract.Documents,
            EventAppendCrashContract.DocumentIndex, true, id)) + Separator + Convert.ToHexString(OrleansSerialization.Serialize(id)));
        var rows = actual.Records.Select(row => Convert.ToHexString(row.Key.Span) + Separator + Convert.ToHexString(row.Value.Span));
        await Assert.That(rows.Order(StringComparer.Ordinal)).IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
    }
}
