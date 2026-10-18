using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ValidateRetainedQueueRetryAdmission(IKeyValueView view, Command<DatabaseCommand> operation,
        CommandOutcomeSelection selection, StoredOutcome outcome, bool preserveBudgetFailure = false)
    {
        if (outcome.Incarnation != Store.Identity.Incarnation
            || outcome.Fingerprint == CommandFingerprint(operation))
        {
            return;
        }
        if (!TryAuthorizeOrdinaryDocumentSubmission(view, operation, out var principal, out _, preserveBudgetFailure)
            || principal is null || outcome.PolicyEpoch != principal.PolicyEpoch)
        {
            return;
        }
        ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome);
    }
}
