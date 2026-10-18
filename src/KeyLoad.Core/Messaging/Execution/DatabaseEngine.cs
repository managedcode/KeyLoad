using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ValidateRetainedQueueRetryAdmission(IKeyValueView view, PrincipalRecord principal,
        Command<DatabaseCommand> operation, CommandOutcomeSelection selection, StoredOutcome outcome)
    {
        if (outcome.Incarnation != Store.Identity.Incarnation
            || outcome.Fingerprint == CommandFingerprint(operation) || outcome.PolicyEpoch != principal.PolicyEpoch)
        {
            return;
        }
        ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome);
    }
}
