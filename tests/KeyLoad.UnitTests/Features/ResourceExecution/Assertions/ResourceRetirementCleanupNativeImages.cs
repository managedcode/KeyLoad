using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementCleanupNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementCleanupNativeImages
{
    internal static Dictionary<string, string> Read(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], ImageLimit);
        if (page.HasMore)
        {
            throw new InvalidOperationException(PageBound);
        }

        return page.Records.ToDictionary(row => Convert.ToHexString(row.Key.Span),
            row => Convert.ToHexString(row.Value.Span), StringComparer.Ordinal);
    });

    internal static async Task SameAsync(ZoneTreeStore store, Dictionary<string, string> expected, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await EqualAsync(expected, Read(store));
    }

    internal static async Task EqualAsync(Dictionary<string, string> expected, Dictionary<string, string> actual)
        => await Assert.That(actual.OrderBy(row => row.Key, StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(expected.OrderBy(row => row.Key, StringComparer.Ordinal).ToArray(), CollectionOrdering.Matching);

    internal static async Task SeededAsync(ResourceRetirementCleanupNativeState state)
    {
        foreach (var partition in state.Partitions)
        {
            foreach (var family in Families)
            {
                var prefix = Convert.ToHexString(KeyLoad.Core.KeySpace.Partition(family, partition, Table));
                var count = state.Original.Keys.Count(key => key.StartsWith(prefix, StringComparison.Ordinal));
                var expected = family is DocumentFamily or IndexFamily or UniqueFamily ? RowsPerPartition : One;
                await Assert.That(count).IsEqualTo(expected);
            }
        }
    }

    internal static async Task FinalAsync(ZoneTreeStore store, ResourceRetirementCleanupNativeState state)
    {
        var expected = state.Original.Where(row => !state.Owned.Contains(row.Key))
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        foreach (var partition in state.Partitions)
        {
            expected.Add(Convert.ToHexString(Checkpoint(partition)),
                Convert.ToHexString(KeyCodec.Encode(FamilyCount, Array.Empty<byte>())));
        }
        await EqualAsync(expected, Read(store));
    }
}
