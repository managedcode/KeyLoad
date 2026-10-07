namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRollupValidation
{
    private const long Empty = 0;
    private const long FirstRevision = 1;
    internal static void Range(DateTimeOffset from, DateTimeOffset until, long revision = Empty)
    {
        if (from.UtcTicks >= until.UtcTicks || revision < Empty || revision == long.MaxValue)
        { throw Errors.Fail(ErrorCode.Validation, SampleRollupProtocol.InvalidRange); }
    }

    internal static void State(SampleRollupState state, DateTimeOffset from, DateTimeOffset until)
    {
        if (state.FormatVersion != SampleRollupNative.Version)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, SampleRollupProtocol.Unsupported); }
        var empty = state.Count == Empty;
        if (state.FromUtcTicks != from.UtcTicks || state.UntilUtcTicks != until.UtcTicks
            || state.FromUtcTicks >= state.UntilUtcTicks || state.Revision < FirstRevision
            || state.SourceSequence < Empty || state.Count < Empty || state.Count > state.SourceSequence
            || !double.IsFinite(state.Sum) || state.Dropped && !empty
            || state.RetentionBeforeUtcTicks is { } floor && (floor < DateTimeOffset.MinValue.UtcTicks || floor > DateTimeOffset.MaxValue.UtcTicks)
            || empty && (state.Sum != Empty || state.Minimum is not null || state.Maximum is not null)
            || !empty && (state.Minimum is not { } minimum || state.Maximum is not { } maximum
                || !double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum))
        { throw Errors.Fail(ErrorCode.Corruption, SampleRollupProtocol.Corrupt); }
    }
}
