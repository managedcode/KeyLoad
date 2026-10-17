using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkJobIdentity
{
    private const int GuidBytes = 16;

    internal static Guid CommandId(PartitionRef partition, string set, string series, Guid windowId,
        long generation, long revision, bool seal, long epoch)
    {
        var digest = SHA256.HashData(KeyCodec.Encode(SampleChunkWorkHint.SerializerAlias, partition.TenantId,
            partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey, set, series, windowId,
            generation, revision, seal ? SampleChunkLifecycleProtocol.SealKind : SampleChunkLifecycleProtocol.MergeKind, epoch));
        return new Guid(digest.AsSpan(SampleChunkLifecycleProtocol.FirstIndex, GuidBytes));
    }

    internal static Mutation Mutation(SampleChunkWorkHint hint) => hint.Seal
        ? new SealSampleChunkWindow(hint.Set, hint.Series, hint.WindowId, hint.Revision)
        : new MergeSampleChunkWindow(hint.Set, hint.Series, hint.WindowId, hint.Revision);
}
