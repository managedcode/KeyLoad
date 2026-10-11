using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorFailedParentLocator
{
    internal static EventVectorFailedParentEligibility? Capture(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, Guid incarnation,
        IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options)
    {
        var open = EventVectorFailedOpenLocator.Capture(transaction, principal, operation,
            incarnation, authorization, options);
        var cleanup = EventVectorFailedCleanupLocator.Capture(transaction, principal, operation,
            incarnation, authorization, options);
        return open is null && cleanup is null ? null : new(open, cleanup);
    }

    internal static void Write(IAtomicTransaction transaction, EventVectorFailedParentEligibility? eligible,
        StoredOutcome actualOutcome, IOptions<DatabaseLimits> options)
    {
        if (eligible is null)
        { return; }
        EventVectorFailedOpenLocator.Write(transaction, eligible.Open, actualOutcome, options);
        EventVectorFailedCleanupLocator.Write(transaction, eligible.Cleanup, actualOutcome, options);
    }
}
