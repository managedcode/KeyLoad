using KeyLoad.Core;

namespace KeyLoad.Replication;

internal static class ReplicaEventVectorSourceAdmit
{
    private const int ContiguousIndexStep = 1;
    private const long BeforeFirstLogPosition = 0;
    private const string Pending = "The original event vector source admission remains in the native journal.";
    private const string Changed = "The original event vector source admission identity is inconsistent.";

    internal static void RequireAbsent(ReplicaState state, EventVectorSourceAdmitProbe? probe,
        ReplicatedOperation operation)
    {
        if (probe is null)
        { return; }
        var last = state.Log.State.LastIndex;
        var snapshot = state.Log.State.Snapshot?.Index ?? BeforeFirstLogPosition;
        if (probe.AppliedPosition < snapshot || probe.AppliedPosition > last)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog); }
        var first = checked(probe.AppliedPosition + ContiguousIndexStep);
        while (first <= last)
        {
            probe.Check();
            var entry = state.Log.ReadEntry(first, probe.AcceptNativeRecord)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
            if (entry.Index != first || entry.Index > last)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog); }
            if (entry.Operation is { } retained)
            { RequireDifferent(retained, operation); }
            first = checked(first + ContiguousIndexStep);
        }
        probe.Check();
    }
    private static void RequireDifferent(ReplicatedOperation retained, ReplicatedOperation original)
    {
        if (retained.Id != original.Id)
        { return; }
        if (retained.Kind != original.Kind || retained.PrincipalId != original.PrincipalId
            || retained.PayloadJson != original.PayloadJson)
        { throw Errors.Fail(ErrorCode.Conflict, Changed); }
        throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Pending);
    }
}
