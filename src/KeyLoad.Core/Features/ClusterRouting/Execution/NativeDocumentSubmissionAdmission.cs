using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Declines speculative eligibility failures without replacing original admission.</summary>
    internal CommandRequest? TryReadOrdinaryDocumentSubmission(ReplicatedOperation operation)
    {
        if (operation.Kind != OperationKind.Batch)
        { return null; }
        try
        { return ReadOrdinaryDocumentSubmission(operation); }
        catch (KeyLoadException) { return null; }
    }

    /// <summary>Normalizes eligible direct calls; production passes its already verified candidate.</summary>
    internal void ValidateSubmissionAdmission(ReplicatedOperation operation)
    {
        if (operation.Kind != OperationKind.Batch)
        { return; }
        ReplicatedOperation normalized;
        try
        { normalized = NormalizeOperation(operation); }
        catch (KeyLoadException) { return; }
        if (TryReadOrdinaryDocumentSubmission(normalized) is { } command)
        { ValidateSubmissionAdmission(normalized, command); }
    }

    /// <summary>Checks authorized changed identity using the original verified operation and candidate.</summary>
    internal void ValidateSubmissionAdmission(ReplicatedOperation operation, CommandRequest command)
    {
        _ = Store.Read(view =>
        {
            if (!TryAuthorizeOrdinaryDocumentSubmission(view, operation, out var principal)
                || principal is null)
            { return false; }
            if (PartitionMoveSourceFenceStorage.Read(view, command.Partition, Limits.MaxBatchBytes) is not null)
            { return false; }
            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            var selection = CommandOutcomeKeyResolver.Select(view, operation.PrincipalId, operation.Id, scope);
            if (selection.Outcome is not { } outcome || outcome.PolicyEpoch != principal.PolicyEpoch
                || outcome.Incarnation != Store.Identity.Incarnation
                || outcome.Fingerprint == CommandFingerprint(operation))
            { return false; }
            ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome);
            return true;
        });
    }

    private bool TryAuthorizeOrdinaryDocumentSubmission(IKeyValueView view, ReplicatedOperation operation,
        out PrincipalRecord? principal)
    {
        principal = null;
        try
        {
            principal = Principal(view, operation.PrincipalId, Clock.GetUtcNow());
            AuthorizeOperation(view, principal, operation);
            return true;
        }
        catch (KeyLoadException) { return false; }
    }

    private CommandRequest? ReadOrdinaryDocumentSubmission(ReplicatedOperation operation)
    {
        if (operation.Kind != OperationKind.Batch || ReadRemoteTransferNativeProof(operation) is not null)
        { return null; }
        var command = Payload<CommandRequest>(operation);
        return !command.Mutations.IsDefaultOrEmpty
            && command.Mutations.All(static mutation => mutation is PutDocument or PatchDocument or DeleteDocument)
                ? command : null;
    }

    private bool IsOrdinaryDocumentSubmission(IKeyValueView view, ReplicatedOperation operation)
    {
        var command = ReadOrdinaryDocumentSubmission(operation);
        return command is not null
            && PartitionMoveSourceFenceStorage.Read(view, command.Partition, Limits.MaxBatchBytes) is null;
    }
}
