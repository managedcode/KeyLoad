using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorFailedCleanupLocator
{
    private const string Invalid = "The original authenticated first cleanup failure requires recovery.";

    internal static EventVectorFailedCleanupEligibility? Capture(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, Guid incarnation,
        IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options)
    {
        if (operation.Kind != OperationKind.EventFeedControl)
        {
            return null;
        }
        var phase = NativeCommandPayload.Read<EventVectorControlPhase>(operation);
        if (phase.Action is not (EventVectorControlAction.AbandonFirstIssuance
            or EventVectorControlAction.CleanupAbandonedIssuance))
        {
            return null;
        }
        var admission = new EventVectorAdmissionPolicy(options);
        EventVectorControlFields.Require(phase, admission);
        if (EventVectorParentLookup.ReadTerminal(transaction, authorization, principal,
            phase.OriginalRequest, incarnation, options, CancellationToken.None) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, Invalid);
        }
        // Authorize every actual retained scope and validate the charged original target
        // before retaining eligibility. The clock/expiry rejection occurs in Execute.
        var original = EventVectorFirstReleaseAdmission.Require(transaction, principal, phase,
            originalOutcome: false, authorization, admission);
        var index = EventVectorFirstCleanupLocator.Create(principal, phase, operation, original, admission);
        return new(index, incarnation, principal.PolicyEpoch);
    }

    internal static void Write(IAtomicTransaction transaction, EventVectorFailedCleanupEligibility? eligible,
        StoredOutcome actualOutcome, IOptions<DatabaseLimits> options)
    {
        if (eligible is null || actualOutcome.Result.Error is null)
        {
            return;
        }
        if (actualOutcome.Incarnation != eligible.Incarnation || actualOutcome.PolicyEpoch != eligible.PolicyEpoch
            || actualOutcome.Fingerprint != eligible.Index.ActualTerminalOutcomeFingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        EventVectorFirstCleanupLocator.Write(transaction, eligible.Index, new EventVectorAdmissionPolicy(options));
    }
}
