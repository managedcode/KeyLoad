using KeyLoad.Core.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes TimeSeriesCommands operations against the caller's database context.</summary>
public static class TimeSeriesCommands
{
    internal const string AppendSamplesOperationKind = "appendSamples";
    internal const string ConflictingSampleContentDetail = "A sample ID was reused with different content.";
    internal const string SampleBeforeRetentionFloorDetail = "A sample cannot be appended before the series retention floor.";
    internal static MutationReceipt Append(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendSamples append, DateTimeOffset evaluatedAt)
    {
        const int SamplesLengthValidationBoundary = 1;
        const string SampleBatchBudgetDetail = "The sample batch exceeds its budget.";
        const string SampleSequenceSpace = "sample-sequence";
        const string NonFiniteSampleValueDetail = "A sample value must be finite.";
        const string SampleIdentitySpace = "sample-id";
        JsonData.Identifier(append.SeriesId);
        var resource = engine.Resource(tx, partition, append.SeriesSet, ResourceKind.TimeSeries);
        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        if (append.Samples.Length < SamplesLengthValidationBoundary || append.Samples.Length > engine.TimeSeriesExecution.MaximumAppendSamples)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, SampleBatchBudgetDetail);
        }

        var tags = JsonData.Validate(append.TagsJson);
        var sequenceKey = KeySpace.Partition(SampleSequenceSpace, partition, append.SeriesSet, append.SeriesId);
        var watermark = SampleRollupRecords.Watermark(tx, partition, append.SeriesSet, append.SeriesId);
        var sequence = watermark.Sequence;
        var chunks = new SampleChunkAppendOwner(engine, tx, partition, append.SeriesSet, append.SeriesId, engine.TimeSeriesOptions);
        foreach (var sample in append.Samples)
        {
            JsonData.Identifier(sample.EventId);
            if (!double.IsFinite(sample.Value))
            {
                throw Errors.Fail(ErrorCode.Validation, NonFiniteSampleValueDetail);
            }

            var idKey = KeySpace.Partition(SampleIdentitySpace, partition, append.SeriesSet, append.SeriesId, sample.EventId);
            var fingerprint = JsonData.Fingerprint(new { sample, Tags = tags });
            if (tx.ReadOwnedValue(idKey) is { } existing)
            {
                if (OrleansSerialization.Deserialize<string>(existing) != fingerprint)
                {
                    throw Errors.Fail(ErrorCode.Conflict, ConflictingSampleContentDetail);
                }

                continue;
            }

            if (watermark.Floor is { } floor && sample.Timestamp.UtcTicks < floor)
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, SampleBeforeRetentionFloorDetail);
            }

            var record = new SampleRecord(append.SeriesId, sample, checked(++sequence), tags);
            chunks.Append(record, evaluatedAt);
            tx.PutRecord(KeySpace.Partition(nameof(sample), partition, append.SeriesSet, append.SeriesId, sample.Timestamp, sequence), record);
            tx.PutRecord(idKey, fingerprint);
        }

        tx.PutRecord(sequenceKey, sequence);
        return new(AppendSamplesOperationKind, append.SeriesSet, append.SeriesId, sequence);
    }
}
