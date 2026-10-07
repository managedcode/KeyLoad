using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.DocumentStorage;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal static class CompositeIndexReferenceModelAssertions
{
    private const string IndexSpace = "index";
    private const string UniqueSpace = "unique";
    private const string Invalid = "Composite index recovery differed from the independent literal matrix.";
    private const int MissingRevision = 0;
    private const int EvidenceMinimum = 1;

    internal static CompositeIndexCrashSnapshot Seeded() => Create(
        [("index-a", "alpha", 1m, 1L), ("index-b", "beta", 2m, 1L), ("index-c", "gamma", 3m, 1L)], false, false);
    internal static CompositeIndexCrashSnapshot Prepared() => Create(
        [("index-a", "delta", 4m, 2L), ("index-b", "epsilon", 5m, 2L)], true, true);
    internal static CompositeIndexCrashSnapshot Recovered() => Create(
        [("index-a", "delta", 4m, 2L), ("index-b", "theta", 8m, 3L), ("index-d", "zeta", 7m, 1L)], true, true);
    internal static CompositeIndexCrashSnapshot Healthy() => Create(
        [("index-a", "healthy", 9m, 3L), ("index-b", "theta", 8m, 3L), ("index-d", "zeta", 7m, 1L)], true, true);

    internal static async Task AssertFileAsync(string root, string fileName, CompositeIndexCrashSnapshot expected)
    {
        var path = Path.Combine(root, fileName);
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            info.Length < EvidenceMinimum || info.Length > CommandIdempotencyCrashContract.EvidenceMaximumBytes)
        { throw new InvalidOperationException(Invalid); }
        var actual = JsonSerializer.Deserialize<CompositeIndexCrashSnapshot>(await File.ReadAllBytesAsync(path), JsonDefaults.Options);
        if (!JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
        { throw new InvalidOperationException(Invalid); }
    }

    private static CompositeIndexCrashSnapshot Create((string Id, string Label, decimal Rank, long Revision)[] live,
        bool tombstone, bool other)
    {
        var documents = new List<CompositeIndexDocumentState>();
        var memberships = new List<CompositeIndexMembership>();
        var images = new List<CompositePhysicalIndexImage>();
        foreach (var partition in new[] { CompositeIndexCrashContract.Partition, CompositeIndexCrashContract.OtherPartition })
        {
            var rows = partition == CompositeIndexCrashContract.Partition ? live :
                other ? [("index-z", "delta", 4m, 1L)] : Array.Empty<(string Id, string Label, decimal Rank, long Revision)>();
            foreach (var id in new[] { "index-a", "index-b", "index-c", "index-d", "index-z" })
            {
                documents.Add(Document(partition, id, rows, tombstone));
            }
            foreach (var label in new[] { "alpha", "beta", "gamma", "delta", "epsilon", "rollback", "zeta", "theta", "healthy" })
            { memberships.Add(new(partition.PartitionKey, label, rows.Where(row => row.Label == label).Select(row => row.Id).Order(StringComparer.Ordinal).ToArray())); }
            images.Add(Image(partition, IndexSpace, CompositeIndexCrashContract.IndexName, rows));
            images.Add(Image(partition, UniqueSpace, CompositeIndexCrashContract.IndexName, rows));
            images.Add(Image(partition, IndexSpace, CompositeIndexCrashContract.RankIndex, rows));
        }
        return new([.. documents], [.. memberships], [.. images]);
    }

    private static CompositeIndexDocumentState Document(PartitionRef partition, string id,
        (string Id, string Label, decimal Rank, long Revision)[] rows, bool tombstone)
    {
        var row = rows.SingleOrDefault(item => item.Id == id);
        var deleted = partition == CompositeIndexCrashContract.Partition && tombstone && id == "index-c";
        var json = row.Id is null ? deleted ? "{}" : null :
            JsonSerializer.Serialize(new { label = row.Label, rank = row.Rank }, JsonDefaults.Options);
        return new(partition.TenantId, partition.DatabaseId, partition.TransactionDomainId,
            partition.PartitionKey, CompositeIndexCrashContract.Collection, id, json,
            row.Id is null ? deleted ? 2L : MissingRevision : row.Revision, deleted);
    }

    private static CompositePhysicalIndexImage Image(PartitionRef partition, string space, string index,
        (string Id, string Label, decimal Rank, long Revision)[] rows)
    {
        var rankOnly = index == CompositeIndexCrashContract.RankIndex;
        var keys = rows.Select(row => (Id: row.Id, Key: KeySpace.Partition(space, partition,
            new object?[] { CompositeIndexCrashContract.Collection, index }.Concat(rankOnly ?
                new object?[] { row.Rank } : [row.Label, row.Rank]).Concat(space == UniqueSpace ?
                Array.Empty<object?>() : [row.Id]).ToArray()))).OrderBy(row => Convert.ToHexString(row.Key), StringComparer.Ordinal).ToArray();
        var after = rankOnly ? rows.Where(row => row.Rank > 4m).OrderBy(row => row.Rank).Select(row => row.Id).ToArray() : [];
        return new(partition.PartitionKey, space, index, keys.Select(row => Convert.ToHexString(row.Key)).ToArray(),
            keys.Select(row => row.Id).ToArray(), after);
    }
}
