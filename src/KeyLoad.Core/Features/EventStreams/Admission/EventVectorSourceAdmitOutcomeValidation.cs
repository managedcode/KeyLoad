using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Revalidates only the exact source-Admit outcome after genuine joined apply.</summary>
    /// <param name="operation">The actual appended original operation.</param>
    /// <param name="originalParentRequestId">The parent identity verified under the original append gate.</param>
    /// <param name="originalSourcePhaseCommandId">The intended source phase verified under that gate.</param>
    /// <param name="appendedIndex">The actual original appended and applied entry index.</param>
    /// <param name="cancellationToken">Original caller cancellation.</param>
    public void RequireEventVectorSourceAdmitOutcome(ReplicatedOperation operation,
        Guid originalParentRequestId, Guid originalSourcePhaseCommandId, long appendedIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var verified = VerifyOperationAuthority(operation);
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, cancellationToken);
        Store.Read(view => ReadAppliedEventVectorSourceAdmit(view, verified, originalParentRequestId,
            originalSourcePhaseCommandId, appendedIndex, work));
    }

    private bool ReadAppliedEventVectorSourceAdmit(IKeyValueView view, ReplicatedOperation verified,
        Guid originalParentRequestId, Guid originalSourcePhaseCommandId, long appendedIndex, ReadExecutionBudget work)
    {
        var bounded = work.CreateView(view);
        var principal = Principal(bounded, verified.PrincipalId, EvaluationClock.GetUtcNow());
        var phase = Payload<EventVectorControlPhase>(verified);
        _ = RequireEventVectorControlView(bounded, principal, phase);
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        var identity = EventVectorControlOperationIdentity.Create(phase, principal.Id, admission);
        if (phase.Action != EventVectorControlAction.AdmitSource || phase.OriginalParentRequestId != originalParentRequestId
            || identity.CommandId != verified.Id || identity.Metadata != verified.PayloadJson
            || phase.IntendedSourcePhase is not { } intended || intended.PhaseCommandId != originalSourcePhaseCommandId)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorControlAuthority); }
        var original = EventVectorControlReplay.ReadOutcome(bounded, principal, phase, admission, Store.Identity.Incarnation);
        if (original is null || original.Error is not null || original.NativeValue is not EventVectorControlResult result
            || result.CommandId != verified.Id || result.SourcePhase is not { } actual
            || result.Receipt.CommandId != verified.Id || result.Receipt.Token.Position != appendedIndex
            || actual.PhaseCommandId != intended.PhaseCommandId
            || !actual.OriginalNativeBody.Span.SequenceEqual(intended.OriginalNativeBody.Span)
            || !actual.OriginalBodyDigest.Span.SequenceEqual(intended.OriginalBodyDigest.Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorControlAuthority); }
        _ = EventVectorSourceMutationValidation.ReadOriginal(actual, admission);
        _ = EventVectorSourceMutationValidation.ReadOriginal(intended, admission);
        work.Check();
        return true;
    }
}
