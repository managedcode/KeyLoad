using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal static void CaptureBm25CorpusReadLookups(IScopedReadCapture source, PrincipalRecord principal,
        PartitionRef partition, string collection, string field, string tokenizer)
    {
        source.CaptureExact(PhysicalShardCatalogRecordSerialization.CatalogKey());
        source.CaptureExact(PhysicalOwnerDirectorySerialization.Key());
        var key = Bm25CorpusKeys.Current(partition, collection, field, tokenizer, principal.Id);
        if (!source.ReadCapturedValue(key, static _ => { }))
        {
            return;
        }
        source.CaptureExact(KeySpace.Partition(OutboxHeadSpace, partition));
    }
}
