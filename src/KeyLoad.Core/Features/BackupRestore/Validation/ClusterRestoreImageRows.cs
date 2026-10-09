namespace KeyLoad.Core;

internal static class ClusterRestoreImageRows
{
    private const string Invalid = "The recovered native restore image contains changed, missing or extra canonical bytes.";

    internal static void Require(ClusterRestoreImageState state, List<byte[]> changed)
    {
        var original = state.Source.VisitRange([], state.Limits.Value.MaxScanRecords, (key, value) =>
        {
            state.Work.Check();
            if (!Changed(changed, key))
            {
                var actual = state.Target.ReadOwnedValue(key.ToArray());
                if (actual is null || !value.SequenceEqual(actual))
                { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            }
            return true;
        }, cancellationToken: state.Work.Cancellation);
        var recovered = state.Target.VisitRange([], state.Limits.Value.MaxScanRecords, (key, value) =>
        {
            state.Work.Check();
            if (!Changed(changed, key))
            {
                var expected = state.Source.ReadOwnedValue(key.ToArray());
                if (expected is null || !value.SequenceEqual(expected))
                { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            }
            return true;
        }, cancellationToken: state.Work.Cancellation);
        if (original.HasMore || original.StoppedByVisitor || recovered.HasMore || recovered.StoppedByVisitor)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Invalid); }
    }

    private static bool Changed(List<byte[]> changed, ReadOnlySpan<byte> key)
    {
        foreach (var candidate in changed)
        { if (key.SequenceEqual(candidate)) { return true; } }
        return false;
    }
}
