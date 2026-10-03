using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidRetentionCutoff = "The time-series retention cutoff is invalid.";
    private const string InvalidRetentionPage = "The time-series retention page is invalid.";
    private const string RetentionCutoffMovedBackwards = "The time-series retention cutoff cannot move backwards.";
    private const string RetentionReadBudgetExceeded = "The time-series retention scan exceeds its byte budget.";

    private MutationReceipt Expire(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition,
        ExpireSamples request, DateTimeOffset now)
    {
        var before = ValidateRetention(tx, principal, partition, request, now);
        var stateKey = SampleRetentionStateReader.Key(partition, request.SeriesSet, request.SeriesId);
        var previous = SampleRetentionStateReader.Read(tx, partition, request.SeriesSet, request.SeriesId);
        if (previous is not null && before.UtcTicks < previous.BeforeUtcTicks)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RetentionCutoffMovedBackwards);
        }

        var prefix = SampleReadKeys.Prefix(partition, request.SeriesSet, request.SeriesId);
        var until = SampleReadKeys.FromInclusive(partition, request.SeriesSet, request.SeriesId, before);
        var deleteKeys = new List<byte[]>(request.MaximumDeletes);
        long examinedBytes = 0;
        void Charge(long bytes)
        {
            if (bytes > Limits.MaxQueryReadBytes - examinedBytes)
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

        var next = new SampleRetentionState(SampleRetentionStateReader.CurrentFormatVersion, before.UtcTicks,
            checked((previous?.PurgedCount ?? 0) + deleteKeys.Count), scan.HasMore);
        tx.PutRecord(stateKey, next);
        return new("expireSamples", request.SeriesSet, request.SeriesId, next.PurgedCount);
    }

    private DateTimeOffset ValidateRetention(IKeyValueView view, PrincipalRecord principal, PartitionRef partition,
        ExpireSamples request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePartition(partition);
        JsonData.Identifier(request.SeriesSet);
        JsonData.Identifier(request.SeriesId);
        Authorization.Require(principal, partition, request.SeriesSet, Capability.SeriesManage);
        _ = Resource(view, partition, request.SeriesSet, ResourceKind.TimeSeries);
        if (request.MaximumDeletes is < 1 or > SampleRetentionDefaults.MaximumDeletes
            || request.MaximumDeletes > Limits.MaxScanRecords)
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

    private static void ValidateRetainedSample(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        PartitionRef partition, ExpireSamples request, DateTimeOffset before)
    {
        SampleRecord record;
        try
        {
            record = NativeSerialization.Deserialize<SampleRecord>(value);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.Corruption, "A retained time-series sample is corrupt.");
        }

        if (record.Sample is null || record.SeriesId != request.SeriesId || record.Sequence < 1
            || record.Sample.Timestamp.UtcTicks >= before.UtcTicks || !double.IsFinite(record.Sample.Value))
        {
            throw Errors.Fail(ErrorCode.Corruption, "A retained time-series sample is corrupt.");
        }

        var canonicalKey = KeySpace.Partition("sample", partition, request.SeriesSet, request.SeriesId,
            record.Sample.Timestamp, record.Sequence);
        if (!key.SequenceEqual(canonicalKey))
        {
            throw Errors.Fail(ErrorCode.Corruption, "A retained time-series sample is corrupt.");
        }
    }
}
