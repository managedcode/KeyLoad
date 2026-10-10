using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkGenerationReader
{
    internal static SampleRecord[] Read(IKeyValueView view, PartitionRef partition, string set, string series,
        SampleChunkWindow window, SampleChunkManifest manifest, IOptions<TimeSeriesExecutionOptions> options,
        SampleChunkReadCharge charge, SampleChunkWork work = default)
    {
        var records = new List<SampleRecord>(manifest.RecordCount + window.CorrectionSequences.Length);
        for (var ordinal = SampleChunkLifecycleProtocol.FirstIndex; ordinal < manifest.BlockDigests.Length; ordinal++)
        {
            var present = view.ReadValue(SampleChunkKeys.Block(partition, set, series, window.WindowId, window.Generation, ordinal),
                bytes =>
                {
                    SampleChunkEncodedDigest.Require(bytes, manifest.BlockDigests[ordinal].Span,
                        work, options.Value.HashChunkBytes);
                    var decoded = SampleChunkCodec.DecodeWithWork(bytes, work, options, SampleChunkCodec.MaximumEncodedBytes);
                    foreach (var record in decoded)
                    { work.Check(); SampleChunkWindowValidation.Record(record, window, series); }
                    records.AddRange(decoded);
                }, charge.Charge);
            if (!present)
            { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
        }
        if (records.Count != manifest.RecordCount)
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
        if (!records.Select(record => record.TagsJson).Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal).SetEquals(manifest.TagJsonValues))
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
        AddCorrections(view, partition, set, series, window, manifest, options.Value.MaximumChunkWindowRecords, charge, records, work);
        if (records.Select(record => record.Sequence).Distinct().Count() != records.Count
            || records.Select(record => record.Sample.EventId).Distinct(StringComparer.Ordinal).Count() != records.Count)
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
        return records.OrderBy(record => record.Sample.Timestamp.UtcTicks).ThenBy(record => record.Sequence).ToArray();
    }

    private static void AddCorrections(IKeyValueView view, PartitionRef partition, string set, string series,
        SampleChunkWindow window, SampleChunkManifest manifest, int maximumRecords,
        SampleChunkReadCharge charge, List<SampleRecord> records, SampleChunkWork work)
    {
        if (manifest.RecordCount + window.CorrectionSequences.Length > maximumRecords)
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
        foreach (var sequence in window.CorrectionSequences)
        {
            work.Check();
            var record = SampleChunkStorage.Require<SampleRecord>(view,
                SampleChunkKeys.Correction(partition, set, series, window.WindowId, sequence), charge);
            SampleChunkWindowValidation.Record(record, window, series);
            if (record.Sequence != sequence || sequence <= manifest.SourceSequence)
            { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
            records.Add(record);
        }
    }
}
