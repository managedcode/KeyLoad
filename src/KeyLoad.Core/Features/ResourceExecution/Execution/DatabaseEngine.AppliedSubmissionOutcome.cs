using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal OperationResult ResolveAppliedSubmissionOutcome(ReplicatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var normalized = NormalizeOperation(operation);
        return Store.Read(view =>
        {
            var scope = CommandOutcomePartitionIdentity.Resolve(normalized);
            var selection = CommandOutcomeKeyResolver.Select(view, normalized.PrincipalId, normalized.Id, scope);
            var outcome = selection.Outcome
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
            try
            {
                ValidateStoredSubmissionIdentity(view, normalized, selection, outcome);
                return ReadValidatedSubmissionOutcome(view, normalized,
                    allowStoredDocumentDenial: true, verifiedSelection: selection);
            }
            catch (KeyLoadException refusal) when (CqrsRuntimeFailures.FindFatal(refusal) is null
                && refusal.Code is ErrorCode.PermissionDenied or ErrorCode.TokenInvalidated
                    or ErrorCode.StaleLease or ErrorCode.Conflict)
            {
                return new(null, refusal.Code, refusal.Message);
            }
        });
    }

    private void ValidateStoredSubmissionIdentity(IKeyValueView view, ReplicatedOperation operation,
        CommandOutcomeSelection selection, StoredOutcome outcome)
    {
        if (outcome.Incarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage);
        }
        if (outcome.Fingerprint != CommandFingerprint(operation))
        {
            throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage);
        }
        CommandOutcomeKeyResolver.ValidateSelectedScope(view, operation, selection);
    }
}
