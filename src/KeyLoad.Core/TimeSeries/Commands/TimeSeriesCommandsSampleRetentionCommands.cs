using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes TimeSeriesCommandsSampleRetentionCommands operations against the caller's database context.</summary>
public static class TimeSeriesCommandsSampleRetentionCommands
{
    internal const string InvalidRetentionCutoff = "The time-series retention cutoff is invalid.";
    internal const string InvalidRetentionPage = "The time-series retention page is invalid.";
    internal const string RetentionCutoffMovedBackwards = "The time-series retention cutoff cannot move backwards.";
    internal const string RetentionReadBudgetExceeded = "The time-series retention scan exceeds its byte budget.";
    internal static MutationReceipt Expire(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, ExpireSamples request, DateTimeOffset now)
    {
        const int ExaminedBytesInitialValue = 0;
        const int PreviousPurgedCountValidationBoundary = 0;
        const string ExpireKindText = "expireSamples";
        var before = engine.ValidateRetention(tx, principal, partition, request, now);
        var stateKey = SampleRetentionStateReader.Key(partition, request.SeriesSet, request.SeriesId);
        var previous = SampleRetentionStateReader.Read(tx, partition, request.SeriesSet, request.SeriesId);
        if (previous is not null && before.UtcTicks < previous.BeforeUtcTicks)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RetentionCutoffMovedBackwards);
        }

        var prefix = SampleReadKeys.Prefix(partition, request.SeriesSet, request.SeriesId);
        var until = SampleReadKeys.FromInclusive(partition, request.SeriesSet, request.SeriesId, before);
        var deleteKeys = new List<byte[]>(request.MaximumDeletes);
        long examinedBytes = ExaminedBytesInitialValue;
        void Charge(long bytes)
        {
            if (bytes > engine.Limits.MaxQueryReadBytes - examinedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, RetentionReadBudgetExceeded);
            }

            examinedBytes += bytes;
        }

        var scan = tx.VisitRange(prefix, request.MaximumDeletes, (key, value) =>
        {
            ValidateRetainedSample(key, value, partition, request, before);
            deleteKeys.Add(key.ToArray());
            return true;
        }, untilKey: until, observer: Charge);
        foreach (var key in deleteKeys)
        {
            tx.Delete(key);
        }

        var next = new SampleRetentionState(SampleRetentionStateReader.CurrentFormatVersion, before.UtcTicks, checked((previous?.PurgedCount ?? PreviousPurgedCountValidationBoundary) + deleteKeys.Count), scan.HasMore);
        tx.PutRecord(stateKey, next);
        return new(ExpireKindText, request.SeriesSet, request.SeriesId, next.PurgedCount);
    }

    internal static DateTimeOffset ValidateRetention(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, ExpireSamples request, DateTimeOffset now)
    {
        const int MaximumDeletesFirstCount = 1;
        ArgumentNullException.ThrowIfNull(request);
        EngineResourceAccess.ValidatePartition(partition);
        JsonData.Identifier(request.SeriesSet);
        JsonData.Identifier(request.SeriesId);
        engine.Authorization.Require(principal, partition, request.SeriesSet, Capability.SeriesManage);
        _ = engine.Resource(view, partition, request.SeriesSet, ResourceKind.TimeSeries);
        if (request.MaximumDeletes is < MaximumDeletesFirstCount or > SampleRetentionDefaults.MaximumDeletes || request.MaximumDeletes > engine.Limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidRetentionPage);
        }

        var before = request.Before.ToUniversalTime();
        if (before > now.ToUniversalTime())
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRetentionCutoff);
        }

        return before;
    }

    internal static void ValidateRetainedSample(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, PartitionRef partition, ExpireSamples request, DateTimeOffset before)
    {
        const string ValidateRetainedSampleDetailText = "A retained time-series sample is corrupt.";
        const int SequenceValidationBoundary = 1;
        const string ValidateRetainedSampleSpaceText = "sample";
        SampleRecord record;
        try
        {
            record = OrleansSerialization.Deserialize<SampleRecord>(value);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.Corruption, ValidateRetainedSampleDetailText);
        }

        if (record.Sample is null || record.SeriesId != request.SeriesId || record.Sequence < SequenceValidationBoundary || record.Sample.Timestamp.UtcTicks >= before.UtcTicks || !double.IsFinite(record.Sample.Value))
        {
            throw Errors.Fail(ErrorCode.Corruption, ValidateRetainedSampleDetailText);
        }

        var canonicalKey = KeySpace.Partition(ValidateRetainedSampleSpaceText, partition, request.SeriesSet, request.SeriesId, record.Sample.Timestamp, record.Sequence);
        if (!key.SequenceEqual(canonicalKey))
        {
            throw Errors.Fail(ErrorCode.Corruption, ValidateRetainedSampleDetailText);
        }
    }
}
