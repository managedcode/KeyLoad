using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementCleanupPageValidation
{
    internal static Guid Require(ResourceRetirementCleanupPageBodyV1 body, DatabaseLimits limits)
    {
        if (body is null || body.SealedState is null || body.Receiver is null || body.Partition is null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        var state = body.SealedState;
        ResourceRetirementWorkValidation.RequireBarrier(state, limits);
        var control = state.Control;
        if (state.Stage != ResourceRetirementStage.BarrierSealed
            || control.Definition.Kind != ResourceKind.Collection || control.Definition.RelationalSchema is null
            || body.Partition.TenantId != control.Lifetime.TenantId
            || body.Partition.DatabaseId != control.Lifetime.DatabaseId
            || body.Partition.TransactionDomainId != control.Lifetime.TransactionDomainId
            || !PhysicalOwnerEntryValidation.Same(body.Receiver,
                ResourceRetirementStateValidation.Member(control, body.Receiver.Owner))
            || !state.FinalInventory!.Entries.Any(entry => entry.Partition == body.Partition
                && PhysicalOwnerEntryValidation.SameOwner(entry.ObservedOwner, body.Receiver.Owner)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        DatabaseEngine.ValidatePartition(body.Partition);
        var previous = body.Previous;
        if (previous is not null && (!ResourceRetirementValidation.Same(previous.Control, control)
            || !ResourceRetirementValidation.Same(previous.Receiver, body.Receiver)
            || previous.Partition != body.Partition || previous.Ordinal <= ResourceRetirementProtocol.Empty || previous.Ordinal == long.MaxValue
            || previous.Complete || previous.FamilyOrdinal < ResourceRetirementProtocol.Empty
            || previous.FamilyOrdinal >= ResourceRetirementTableCleanupFamilies.Count
            || previous.AfterKey.Length > limits.MaxBatchBytes))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        if (body.Attempt is null || body.MaximumReadBytes <= ResourceRetirementProtocol.Empty
            || body.MaximumReadBytes > limits.MaxQueryReadBytes
            || body.MaximumExaminedRecords <= ResourceRetirementProtocol.Empty
            || body.MaximumExaminedRecords > limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }

        var context = ResourceRetirementCleanupAttemptScope.Context(body);
        ResourceRetirementAttemptValidation.Witness(body.Attempt, context, limits);
        ResourceRetirementValidation.Bound(body, limits);
        return body.Attempt.OriginalCommandId;
    }
}
