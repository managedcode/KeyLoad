using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkManifestReader
{
    internal static SampleChunkManifest Read(IKeyValueView view, PartitionRef partition, string set,
        string series, SampleChunkWindow window, int maximumRecords, SampleChunkReadCharge charge, Microsoft.Extensions.Options.IOptions<TimeSeriesExecutionOptions> options, SampleChunkWork work = default)
    {
        var envelope = SampleChunkStorage.Require<SampleChunkManifestEnvelope>(view,
            SampleChunkKeys.Manifest(partition, set, series, window.WindowId, window.Generation), charge);
        SampleChunkEncodedDigest.Require(envelope.Encoded.Span, envelope.Digest.Span, work, options.Value.HashChunkBytes);
        var manifest = NativeSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        if (manifest.Version != SampleChunkLifecycleProtocol.Version)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, SampleChunkLifecycleProtocol.Unsupported); }
        if (manifest.WindowId != window.WindowId || manifest.Generation != window.Generation
            || manifest.FromUtcTicks != window.FromUtcTicks || manifest.UntilUtcTicks != window.UntilUtcTicks
            || manifest.RecordCount < SampleChunkLifecycleProtocol.First || manifest.RecordCount > maximumRecords
            || manifest.SourceSequence > window.SourceSequence || manifest.SourceSequence < SampleChunkLifecycleProtocol.First
            || manifest.BlockDigests.IsDefaultOrEmpty || manifest.TagJsonValues.IsDefaultOrEmpty
            || manifest.BlockDigests.Length != (manifest.RecordCount + SampleChunkWire.MaximumRecords
                - SampleChunkLifecycleProtocol.First) / SampleChunkWire.MaximumRecords
            || manifest.BlockDigests.Any(digest => digest.Length != System.Security.Cryptography.SHA256.HashSizeInBytes))
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
        return manifest;
    }
}
