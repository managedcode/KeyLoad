using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class SearchReadBudgetCost
{
    private const string Principal = "root";
    private const string TextField = "/text";
    internal static long TextMetadata(ZoneTreeStore store, PartitionRef partition, string collection)
        => store.Read(view =>
        {
            var resource = KeySpace.Resource(partition.TenantId, partition.DatabaseId, collection);
            var head = Bm25CorpusKeys.Current(partition, collection, TextField,
                TextProjectionProtocol.TokenizerVersion, Principal);
            var resourceValue = view.ReadOwnedValue(resource)
                ?? throw new InvalidOperationException("The original configured resource is missing.");
            if (view.ReadOwnedValue(head) is not null)
            {
                throw new InvalidOperationException("The original kernel fixture must have no retained BM25 head.");
            }
            return checked((long)resource.Length + resourceValue.Length + head.Length);
        });
    internal static long Rows(ZoneTreeStore store, byte[] prefix)
        => store.Read(view =>
        {
            var page = view.Scan(prefix, MaximumSeedRows);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The bounded original seed rows are incomplete.");
            }
            return page.Records.Sum(row => checked((long)row.Key.Length + row.Value.Length));
        });
    internal static long Point(ZoneTreeStore store, byte[] key)
        => store.Read(view => checked((long)key.Length + (view.ReadOwnedValue(key)?.LongLength ?? EmptyBytes)));
    private const int MaximumSeedRows = 100;
    private const long EmptyBytes = 0;
}
