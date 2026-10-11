using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorFirstIssuanceDisposition
{
    internal static EventVectorFirstIssuance Execute(IAtomicTransaction transaction,
        PrincipalRecord principal, EventVectorControlPhase phase, ReplicatedOperation operation,
        IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(options);
        var admission = new EventVectorAdmissionPolicy(options);
        var original = EventVectorFirstReleaseAdmission.Require(transaction, principal, phase,
            originalOutcome: false, authorization, admission);
        if (phase.Action == EventVectorControlAction.CleanupAbandonedIssuance)
        { return EventVectorFirstIssuanceWriter.Release(transaction, original, operation, admission); }
        var abandoned = original with { State = EventVectorFirstIssuanceState.Abandoned };
        EventVectorFirstIssuanceWriter.Replace(transaction, original, abandoned, admission);
        return abandoned;
    }
}
