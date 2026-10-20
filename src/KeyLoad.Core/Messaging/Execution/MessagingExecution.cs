using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingExecution operations against the caller's database context.</summary>
public static class MessagingExecution
{
    internal static void ValidateRetainedQueueRetryAdmission(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, CommandOutcomeSelection selection, StoredOutcome outcome)
    {
        if (outcome.Incarnation != engine.Store.Identity.Incarnation || outcome.Fingerprint == OperationProtocol.CommandFingerprint(operation) || outcome.PolicyEpoch != principal.PolicyEpoch)
        {
            return;
        }

        engine.ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome);
    }
}
