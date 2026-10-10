namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWindowValidation
{
    internal static void Identity(string series, Guid id)
    {
        JsonData.Identifier(series);
        if (id == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, SampleChunkLifecycleProtocol.Invalid); }
    }

    internal static void State(SampleChunkWindow window, Guid id, int recordLimit, int correctionLimit)
    {
        if (window.Version != SampleChunkLifecycleProtocol.Version)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, SampleChunkLifecycleProtocol.Unsupported); }
        if (window.WindowId != id || id == Guid.Empty || window.FromUtcTicks < DateTimeOffset.MinValue.UtcTicks
            || window.UntilUtcTicks > DateTimeOffset.MaxValue.UtcTicks || window.FromUtcTicks >= window.UntilUtcTicks
            || window.Revision < SampleChunkLifecycleProtocol.First || window.SourceSequence < SampleChunkLifecycleProtocol.Absent
            || window.Generation < SampleChunkLifecycleProtocol.Absent || !Enum.IsDefined(window.State)
            || string.IsNullOrEmpty(window.CreatorPrincipalId) || window.OpenRecords.IsDefault
            || window.CorrectionSequences.IsDefault || window.OpenRecords.Length > recordLimit
            || window.CorrectionSequences.Length > correctionLimit
            || window.CorrectionSequences.Any(sequence => sequence < SampleChunkLifecycleProtocol.First
                || sequence > window.SourceSequence)
            || !window.CorrectionSequences.SequenceEqual(window.CorrectionSequences.Order())
            || window.CorrectionSequences.Distinct().Count() != window.CorrectionSequences.Length
            || window.State == SampleChunkWindowState.Open && window.Generation != SampleChunkLifecycleProtocol.Absent
            || window.State != SampleChunkWindowState.Open && !window.OpenRecords.IsEmpty
            || window.State == SampleChunkWindowState.Sealed && window.Generation < SampleChunkLifecycleProtocol.First
            || window.State == SampleChunkWindowState.Dropped && !window.CorrectionSequences.IsEmpty
            || window.State != SampleChunkWindowState.Sealed && window.MaintenanceCommandId != Guid.Empty
            || window.State == SampleChunkWindowState.Sealed
                && window.CorrectionSequences.IsEmpty != (window.MaintenanceCommandId == Guid.Empty)
            || window.RetentionBeforeUtcTicks is { } floor
                && (floor < DateTimeOffset.MinValue.UtcTicks || floor > DateTimeOffset.MaxValue.UtcTicks)
            || window.OpenRecords.Any(record => record is null || record.Sample is null)
            || window.OpenRecords.Select(record => record.Sequence).Distinct().Count() != window.OpenRecords.Length
            || window.OpenRecords.Select(record => record.Sample.EventId).Distinct(StringComparer.Ordinal).Count() != window.OpenRecords.Length)
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
    }

    internal static void Record(SampleRecord record, SampleChunkWindow window, string series)
    {
        if (record is null || record.SeriesId != series || record.Sample is null || record.Sequence < SampleChunkLifecycleProtocol.First
            || record.Sequence > window.SourceSequence || string.IsNullOrEmpty(record.Sample.EventId)
            || record.TagsJson is null || !double.IsFinite(record.Sample.Value)
            || record.Sample.Timestamp.UtcTicks < window.FromUtcTicks || record.Sample.Timestamp.UtcTicks >= window.UntilUtcTicks)
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
    }
}
