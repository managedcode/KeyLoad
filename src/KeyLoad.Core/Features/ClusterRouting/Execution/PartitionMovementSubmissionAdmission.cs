using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal void ValidatePartitionMovementSubmissionAdmission(ReplicatedOperation operation)
    {
        if (operation.Kind != OperationKind.PartitionMovementPhase)
        { return; }
        _ = Store.Read(view =>
        {
            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            var selection = CommandOutcomeKeyResolver.Select(view, operation.PrincipalId, operation.Id, scope);
            if (selection.Outcome is not { } outcome
                || outcome.Fingerprint == CommandFingerprint(operation))
            { return false; }
            var principal = Principal(view, operation.PrincipalId, Clock.GetUtcNow());
            AuthorizeOperation(view, principal, operation);
            ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome);
            return true;
        });
    }
}
