using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private MutationReceipt Append(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendSamples append)
    {
        JsonData.Identifier(append.SeriesId);
        var resource = Resource(tx, partition, append.SeriesSet, ResourceKind.TimeSeries);
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        if (append.Samples.Length is < 1 or > 10_000)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The sample batch exceeds its budget.");
        }

        var tags = JsonData.Validate(append.TagsJson, Limits);
        var sequenceKey = KeySpace.Partition("sample-sequence", partition, append.SeriesSet, append.SeriesId);
        var sequence = tx.ReadOwnedValue(sequenceKey) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : 0;
        var retention = SampleRetentionStateReader.Read(tx, partition, append.SeriesSet, append.SeriesId);
        foreach (var sample in append.Samples)
        {
            JsonData.Identifier(sample.EventId);
            if (!double.IsFinite(sample.Value))
            {
                throw Errors.Fail(ErrorCode.Validation, "A sample value must be finite.");
            }

            var idKey = KeySpace.Partition("sample-id", partition, append.SeriesSet, append.SeriesId, sample.EventId);
            var fingerprint = JsonData.Fingerprint(new { sample, Tags = tags });
            if (tx.ReadOwnedValue(idKey) is { } existing)
            {
                if (NativeSerialization.Deserialize<string>(existing) != fingerprint)
                {
                    throw Errors.Fail(ErrorCode.Conflict, "A sample ID was reused with different content.");
                }

                continue;
            }
            if (retention is not null && sample.Timestamp.UtcTicks < retention.BeforeUtcTicks)
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, "A sample cannot be appended before the series retention floor.");
            }
            var record = new SampleRecord(append.SeriesId, sample, checked(++sequence), tags);
            tx.PutRecord(KeySpace.Partition("sample", partition, append.SeriesSet, append.SeriesId, sample.Timestamp, sequence), record);
            tx.PutRecord(idKey, fingerprint);
        }
        tx.PutRecord(sequenceKey, sequence);
        return new("appendSamples", append.SeriesSet, append.SeriesId, sequence);
    }
}
