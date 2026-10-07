using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CompositePhysicalIndexObservation
{
    private const decimal ExclusiveRankBound = 4m;
    private const int MaximumRecords = 64;
    private const string IndexSpace = "index";
    private const string UniqueSpace = "unique";
    private const string AfterId = "zzzz";
    private const string Truncated = "The complete composite index observation exceeded its fixture bound.";

    internal static CompositePhysicalIndexImage[] Capture(DatabaseEngine database)
        => database.Store.Read(view => new[] { CompositeIndexCrashContract.Partition, CompositeIndexCrashContract.OtherPartition }
            .SelectMany(partition => new[]
            {
                Read(view, partition, IndexSpace, CompositeIndexCrashContract.IndexName),
                Read(view, partition, UniqueSpace, CompositeIndexCrashContract.IndexName),
                Read(view, partition, IndexSpace, CompositeIndexCrashContract.RankIndex)
            }).ToArray());

    private static CompositePhysicalIndexImage Read(IKeyValueView view, PartitionRef partition, string space, string index)
    {
        var prefix = KeySpace.Partition(space, partition, CompositeIndexCrashContract.Collection, index);
        var page = view.Scan(prefix, MaximumRecords);
        if (page.HasMore)
        { throw new InvalidOperationException(Truncated); }
        var after = Array.Empty<string>();
        if (index == CompositeIndexCrashContract.RankIndex)
        {
            var bound = KeySpace.Partition(space, partition, CompositeIndexCrashContract.Collection, index, ExclusiveRankBound, AfterId);
            var range = view.Scan(prefix, MaximumRecords, bound);
            if (range.HasMore)
            { throw new InvalidOperationException(Truncated); }
            after = range.Records.Select(row => NativeSerialization.Deserialize<string>(row.Value.Span)).ToArray();
        }
        return new(partition.PartitionKey, space, index,
            page.Records.Select(row => Convert.ToHexString(row.Key.Span)).ToArray(),
            page.Records.Select(row => NativeSerialization.Deserialize<string>(row.Value.Span)).ToArray(), after);
    }
}
