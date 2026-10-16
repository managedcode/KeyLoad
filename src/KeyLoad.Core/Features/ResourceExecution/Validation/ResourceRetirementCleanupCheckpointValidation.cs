using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementCleanupCheckpointValidation
{
    internal static void Require(ResourceRetirementCleanupCheckpointV1 checkpoint, DatabaseLimits limits)
    {
        try
        {
            RequireCore(checkpoint, limits);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Validation or ErrorCode.TokenInvalidated
            or ErrorCode.OwnershipLost or ErrorCode.RecoveryRequired or ErrorCode.BudgetExceeded)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    private static void RequireCore(ResourceRetirementCleanupCheckpointV1 checkpoint, DatabaseLimits limits)
    {
        if (checkpoint is null || checkpoint.Control is null || checkpoint.Receiver is null
            || checkpoint.Partition is null || checkpoint.Receipt is null || checkpoint.Ordinal <= 0
            || checkpoint.FamilyOrdinal < 0 || checkpoint.FamilyOrdinal > ResourceRetirementTableCleanupFamilies.Count
            || checkpoint.Complete != (checkpoint.FamilyOrdinal == ResourceRetirementTableCleanupFamilies.Count)
            || checkpoint.ExaminedBytes < 0 || checkpoint.ExaminedRecords < 0 || checkpoint.AttemptId == Guid.Empty
            || checkpoint.AfterKey.Length > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var control = checkpoint.Control;
        ResourceRetirementValidation.Control(control, limits);
        var member = ResourceRetirementStateValidation.Member(control, checkpoint.Receiver.Owner);
        if (!PhysicalOwnerEntryValidation.Same(member, checkpoint.Receiver))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        DatabaseEngine.ValidatePartition(checkpoint.Partition);
        _ = ResourceRetirementTableCleanupKeys.Prefix(control.Lifetime, checkpoint.Partition,
            new(checkpoint.FamilyOrdinal, checkpoint.AfterKey.ToArray()));
        var receipt = checkpoint.Receipt;
        if (receipt.Version != ResourceRetirementProtocol.Version
            || receipt.RetirementId != control.Request.RetirementId
            || receipt.Phase != ResourceRetirementPhase.CleanupPage || receipt.AppliedPosition <= 0
            || receipt.CommandId != ResourceRetirementCleanupPageIdentity.For(control, member,
                checkpoint.Partition, checkpoint.Ordinal, checkpoint.AttemptId)
            || receipt.IntentDigest != control.IntentDigest
            || !ResourceRetirementValidation.Same(receipt.Lifetime, control.Lifetime)
            || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, member.Owner))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementOutcomeValidation.Identity(checkpoint.OutcomeIdentity, limits);
        ResourceRetirementValidation.Bound(checkpoint, limits);
    }
}
