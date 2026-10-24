using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleChunkGenerationWriter
{
    internal static SampleChunkManifest Write(IAtomicTransaction tx, PartitionRef partition, string set,
        string series, SampleChunkWindow window, ReadOnlySpan<SampleRecord> records,
        IOptions<TimeSeriesExecutionOptions> options, int maximumBytes)
    {
        var digests = ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>();
        for (var offset = SampleChunkLifecycleProtocol.FirstIndex; offset < records.Length;
            offset += SampleChunkWire.MaximumRecords)
        {
            var size = Math.Min(SampleChunkWire.MaximumRecords, records.Length - offset);
            var encoded = SampleChunkCodec.EncodeOrdered(records.Slice(offset, size), options,
                Math.Min(maximumBytes, SampleChunkCodec.MaximumEncodedBytes));
            var key = SampleChunkKeys.Block(partition, set, series, window.WindowId, window.Generation, digests.Count);
            if (tx.ReadOwnedValue(key) is not null)
            {
                throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt);
            }
            tx.Put(key, encoded);
            digests.Add(SHA256.HashData(encoded));
        }
        var tags = Tags(records);
        var manifest = new SampleChunkManifest(SampleChunkLifecycleProtocol.Version, window.WindowId,
            window.Generation, window.FromUtcTicks, window.UntilUtcTicks, window.SourceSequence, records.Length,
            digests.ToImmutable(), tags, window.RetentionBeforeUtcTicks);
        var manifestKey = SampleChunkKeys.Manifest(partition, set, series, window.WindowId, window.Generation);
        if (tx.ReadOwnedValue(manifestKey) is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt);
        }
        var bytes = OrleansSerialization.Serialize(manifest);
        SampleChunkStorage.Write(tx, manifestKey, new SampleChunkManifestEnvelope(bytes, SHA256.HashData(bytes)), maximumBytes);
        return manifest;
    }

    private static ImmutableArray<string> Tags(ReadOnlySpan<SampleRecord> records)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tags = ImmutableArray.CreateBuilder<string>();
        foreach (var record in records)
        {
            if (seen.Add(record.TagsJson))
            {
                tags.Add(record.TagsJson);
            }
        }
        return tags.ToImmutable();
    }
}
