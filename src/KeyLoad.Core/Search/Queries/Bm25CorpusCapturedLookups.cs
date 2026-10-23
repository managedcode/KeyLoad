using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes Bm25CorpusCapturedLookups operations against the caller's database context.</summary>
public static class Bm25CorpusCapturedLookups
{
    internal static void CaptureBm25CorpusReadLookups(IScopedReadCapture source, PrincipalRecord principal, PartitionRef partition, string collection, string field, string tokenizer)
    {
        var key = Bm25CorpusKeys.Current(partition, collection, field, tokenizer, principal.Id);
        if (!source.ReadCapturedValue(key, static _ =>
        {
        }))
        {
            return;
        }

        source.CaptureExact(KeySpace.Partition(ProjectionOutbox.OutboxHeadSpace, partition));
    }
}
