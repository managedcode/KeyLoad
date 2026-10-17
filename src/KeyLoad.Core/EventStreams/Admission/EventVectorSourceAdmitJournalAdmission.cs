using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Classifies and authorizes only the original private source-Admit append inspection.</summary>
    /// <param name="operation">The actual original verified native operation.</param>
    /// <param name="cancellationToken">Original submission cancellation.</param>
    /// <returns>The owning grant, or null for the unchanged non-source-Admit path.</returns>
    public EventVectorSourceAdmitProbe? OpenEventVectorSourceAdmitProbe(ReplicatedOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Kind != OperationKind.EventFeedControl)
        {
            return null;
        }
        var verified = VerifyOperationAuthority(operation);
        RequireNativeBudget(System.Text.Encoding.UTF8.GetByteCount(verified.PayloadJson));
        if (EventVectorControlMetadataAction.Read(verified.PayloadJson) != EventVectorControlAction.AdmitSource)
        {
            return null;
        }
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, cancellationToken);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var probe = new EventVectorSourceAdmitProbe(new EventVectorFirstAdmitProbe(work, grant));
        try
        {
            using var scope = work.EnterReadGrant(grant);
            Store.Read(view => ReadAuthorizedEventVectorSourceAdmit(view, verified, work, probe));
            return probe;
        }
        catch (Exception primary)
        {
            probe.DisposeAfterFailure(primary);
            throw;
        }
    }

    private bool ReadAuthorizedEventVectorSourceAdmit(IKeyValueView view,
        ReplicatedOperation verified, ReadExecutionBudget work, EventVectorSourceAdmitProbe probe)
    {
        var bounded = work.CreateView(view);
        var principal = Principal(bounded, verified.PrincipalId, EvaluationClock.GetUtcNow());
        var phase = Payload<EventVectorControlPhase>(verified);
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        if (phase.Action != EventVectorControlAction.AdmitSource || phase.IntendedSourcePhase is not { } intended
            || phase.OriginalParentRequestId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorControlAuthority);
        }
        work.ConstrainLifetime(phase.OriginalParentExpiresAt);
        _ = RequireEventVectorControlView(bounded, principal, phase);
        var identity = EventVectorControlOperationIdentity.Create(phase, principal.Id, admission);
        if (verified.Id != identity.CommandId || verified.PayloadJson != identity.Metadata)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorControlAuthority);
        }
        var original = EventVectorControlReplay.ReadOutcome(bounded, principal, phase, admission, Store.Identity.Incarnation);
        if (original is null)
        {
            EventVectorParentCommit.RequirePrevious(bounded, principal, phase, Store.Identity.Incarnation, Authorization, OperationLimitsOptions);
        }
        _ = EventVectorSourceMutationValidation.ReadOriginal(intended, admission);
        probe.OriginalParentRequestId = phase.OriginalParentRequestId;
        probe.OriginalSourcePhaseCommandId = intended.PhaseCommandId;
        probe.OriginalOutcome = original;
        probe.AppliedPosition = bounded.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? NativeSerialization.Deserialize<long>(bytes) : BeforeFirstAdmitPosition;
        return true;
    }
}
