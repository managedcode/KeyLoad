using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkRepresentationRetirement
{
    internal static void Delete(IAtomicTransaction tx, PartitionRef partition, string set, string series,
        SampleChunkWindow original, SampleChunkManifest manifest)
    {
        for (var ordinal = SampleChunkLifecycleProtocol.FirstIndex; ordinal < manifest.BlockDigests.Length; ordinal++)
        { tx.Delete(SampleChunkKeys.Block(partition, set, series, original.WindowId, original.Generation, ordinal)); }
        foreach (var sequence in original.CorrectionSequences)
        { tx.Delete(SampleChunkKeys.Correction(partition, set, series, original.WindowId, sequence)); }
        tx.Delete(SampleChunkKeys.Manifest(partition, set, series, original.WindowId, original.Generation));
    }
}
