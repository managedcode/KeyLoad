using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string AppendSamplesOperationKind = "appendSamples";

    private const string ConflictingSampleContentDetail = "A sample ID was reused with different content.";
    private const string SampleBeforeRetentionFloorDetail = "A sample cannot be appended before the series retention floor.";

    private MutationReceipt Append(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendSamples append)
    {
        const int SamplesLengthValidationBoundary = 1;
        const string SampleBatchBudgetDetail = "The sample batch exceeds its budget.";
        const string SampleSequenceSpace = "sample-sequence";
        const int AppendAbsentCount = 0;
        const string NonFiniteSampleValueDetail = "A sample value must be finite.";
        const string SampleIdentitySpace = "sample-id";

        JsonData.Identifier(append.SeriesId);
        var resource = Resource(tx, partition, append.SeriesSet, ResourceKind.TimeSeries);
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        if (append.Samples.Length < SamplesLengthValidationBoundary || append.Samples.Length > timeSeriesExecution.MaximumAppendSamples)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, SampleBatchBudgetDetail);
        }

        var tags = JsonData.Validate(append.TagsJson, Limits);
        var sequenceKey = KeySpace.Partition(SampleSequenceSpace, partition, append.SeriesSet, append.SeriesId);
        var sequence = tx.ReadOwnedValue(sequenceKey) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : AppendAbsentCount;
        var retention = SampleRetentionStateReader.Read(tx, partition, append.SeriesSet, append.SeriesId);
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
                if (NativeSerialization.Deserialize<string>(existing) != fingerprint)
                {
                    throw Errors.Fail(ErrorCode.Conflict, ConflictingSampleContentDetail);
                }

                continue;
            }
            if (retention is not null && sample.Timestamp.UtcTicks < retention.BeforeUtcTicks)
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, SampleBeforeRetentionFloorDetail);
            }
            var record = new SampleRecord(append.SeriesId, sample, checked(++sequence), tags);
            tx.PutRecord(KeySpace.Partition(nameof(sample), partition, append.SeriesSet, append.SeriesId, sample.Timestamp, sequence), record);
            tx.PutRecord(idKey, fingerprint);
        }
        tx.PutRecord(sequenceKey, sequence);
        return new(AppendSamplesOperationKind, append.SeriesSet, append.SeriesId, sequence);
    }
}
