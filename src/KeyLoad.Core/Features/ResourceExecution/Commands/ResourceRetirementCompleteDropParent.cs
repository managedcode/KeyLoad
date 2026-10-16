using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 CompleteResourceRetirementDropParent(IAtomicTransaction tx,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementCompleteDropParentBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        var parent = ReadRetirementDropParent(tx, body.Lookup, principal.Id)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        if (parent.OriginalCompletion is not null)
        {
            // Never substitute a stored parent row for the original native completion outcome.
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        }
        var state = ResourceRetirementStorage.ReadControl(tx, parent.RetirementRequest, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        var proven = RequireResourceRetirementFinalizedSource(tx, state.Control);
        if (!ResourceRetirementValidation.Same(proven, state))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementCompletionValidation.RequireCompleted(proven, Limits);
        var canonical = RequireRetirementDropCompletion(parent, proven);
        var receipt = new ResourceRetirementPhaseReceiptV1(ResourceRetirementProtocol.Version, parent.Lookup.CommandId,
            ResourceRetirementPhase.CompleteDropParent, operation.Id, parent.Lifetime,
            parent.PersistReceipt.PhysicalOwner, position, parent.PersistReceipt.IntentDigest);
        var result = new DropTableResult(parent.Lookup.Request.TenantId, parent.Lookup.Request.DatabaseId,
            parent.Lookup.Request.ResourceName, canonical);
        var completed = parent with
        {
            ResultJson = JsonSerializer.Serialize(result, JsonDefaults.Options),
            OriginalCompletion = operation,
            CompleteReceipt = receipt,
            CompletionPolicyEpoch = principal.PolicyEpoch
        };
        RequireRetirementDropParentShape(completed);
        RequireRetirementDropParentOriginal(completed, operation, receipt, ResourceRetirementPhase.CompleteDropParent);
        ResourceRetirementDropParentStorage.Write(tx, completed, Limits);
        return new(receipt, null, null)
        {
            DropParent = completed
        };
    }

    private CommitReceipt RequireRetirementDropCompletion(ResourceRetirementDropParentV1 parent,
        ResourceRetirementStateV1 state)
    {
        if (state.Stage != ResourceRetirementStage.Completed || state.CompletionReceipt is null
            || state.Finalization?.CanonicalReceipt is not { } canonical
            || !ResourceRetirementValidation.Same(state.Control.Request, parent.RetirementRequest)
            || !ResourceRetirementValidation.Same(state.Control.Lifetime, parent.Lifetime)
            || !ResourceRetirementValidation.Same(state.Control.Definition, parent.Definition)
            || state.Control.PrincipalId != parent.PrincipalId
            || canonical.CommandId != state.Finalization.Receipt.CommandId
            || state.OwnerReleaseReceipts.IsDefault
            || state.OwnerReleaseReceipts.Length != state.Control.Directory.Owners.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        }
        return canonical;
    }
}
