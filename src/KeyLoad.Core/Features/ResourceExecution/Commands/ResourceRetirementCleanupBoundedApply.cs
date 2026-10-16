using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceRetirementCleanupCheckpointV1 ApplyResourceRetirementCleanupPage(
        IAtomicTransaction transaction, PrincipalRecord principal, ReplicatedOperation operation,
        ResourceRetirementCleanupPageBodyV1 body, long position)
    {
        var expected = ResourceRetirementCleanupPageValidation.Require(body, Limits);
        if (operation.Id != expected || position <= ResourceRetirementProtocol.Empty
            || operation.Kind != OperationKind.ResourceRetirementPhase
            || body.MaximumReadBytes <= ResourceRetirementProtocol.Empty || body.MaximumReadBytes > Limits.MaxQueryReadBytes
            || body.MaximumExaminedRecords <= ResourceRetirementProtocol.Empty || body.MaximumExaminedRecords > Limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        var work = new ResourceRetirementCleanupReadView(transaction,
            body.MaximumReadBytes, body.MaximumExaminedRecords);
        var control = body.SealedState.Control;
        RequireRetirementReceiver(work, control, body.Receiver);
        var fence = ResourceRetirementStorage.ReadFence(work, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        RequireSameRetirement(fence.Control, control);
        _ = RequirePreparedResourceRetirementCleanupPage(work, operation, body);
        RequireCleanupOwnerWork(work, body, fence);
        var previous = ResourceRetirementCleanupStorage.Read(work, control, body.Receiver, body.Partition, Limits);
        if (!ResourceRetirementValidation.Same(previous, body.Previous))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        if (transaction is not AtomicPartitionRosterTransaction roster)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ResourceRetirementCleanupScope.CandidateUnsupported);
        }
        roster.UseRetirementCleanup(work, body, operation, Store.Identity.Incarnation);
        var outcomeKey = KeySpace.GlobalOutcome(operation.PrincipalId, operation.Id);
        if (work.ReadOwnedValue(outcomeKey) is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var receipt = RetirementReceipt(work, control, operation, ResourceRetirementPhase.CleanupPage, position);
        var available = CleanupScanLimits(work);
        var plan = ResourceRetirementTableCleanupPage.Plan(work, control.Lifetime, body.Partition,
            new(previous?.FamilyOrdinal ?? ResourceRetirementProtocol.Empty, previous?.AfterKey.ToArray()),
            OperationLimitsOptions, available, ResourceRetirementProtocol.Empty, ResourceRetirementProtocol.Empty, stopAtCapacity: true);
        var selected = SelectCleanupCandidate(roster, principal, operation, body, receipt, plan, work, outcomeKey);
        selected.Selection.Apply(transaction);
        transaction.Put(selected.Metadata.Mutations[ResourceRetirementProtocol.Empty].Key.ToArray(),
            selected.Metadata.Mutations[ResourceRetirementProtocol.Empty].Value!.Value.ToArray());
        return selected.Metadata.Checkpoint;
    }

    private (long MaximumBytes, int MaximumRecords) CleanupScanLimits(ResourceRetirementCleanupReadView work)
    {
        if (work.RemainingBytes <= ResourceRetirementProtocol.Empty || work.RemainingRecords <= ResourceRetirementProtocol.Empty)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementTableCleanupFamilies.Capacity);
        }
        return (Math.Min(Limits.MaxQueryReadBytes, work.RemainingBytes),
            Math.Min(Limits.MaxScanRecords, work.RemainingRecords));
    }
}
