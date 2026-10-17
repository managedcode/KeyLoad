using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>A closed page coordinate; logical page ordinal is distinct from retry ordinal.</summary>
internal static class ResourceRetirementCleanupAttemptScope
{
    internal static ResourceRetirementAttemptContextV1 Context(ResourceRetirementControlV1 control,
        RegisteredPhysicalOwnerV1 receiver, PartitionRef partition, long ordinal)
        => new(ResourceRetirementAttemptPurpose.CleanupPage, control, null, receiver)
        {
            Page = new(partition, ordinal)
        };

    internal static ResourceRetirementAttemptContextV1 Context(ResourceRetirementCleanupPageBodyV1 body)
        => Context(body.SealedState.Control, body.Receiver, body.Partition,
            body.Previous is null ? ResourceRetirementProtocol.First : checked(body.Previous.Ordinal + ResourceRetirementProtocol.First));

    internal static void Require(ResourceRetirementAttemptContextV1 context, DatabaseLimits limits)
    {
        var control = context.Retirement!;
        var page = context.Page!;
        var receiver = ResourceRetirementStateValidation.Member(control, context.Receiver!.Owner);
        if (!PhysicalOwnerEntryValidation.Same(receiver, context.Receiver) || page.Partition is null
            || page.Ordinal <= ResourceRetirementProtocol.Empty
            || page.Partition.TenantId != control.Lifetime.TenantId
            || page.Partition.DatabaseId != control.Lifetime.DatabaseId
            || page.Partition.TransactionDomainId != control.Lifetime.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        DatabaseEngine.ValidatePartition(page.Partition);
        ResourceRetirementValidation.Bound(page, limits);
    }

    internal static ResourceRetirementPhase OriginalPhase(ResourceRetirementAttemptContextV1 context)
        => context.Purpose switch
        {
            ResourceRetirementAttemptPurpose.SealBarrier => ResourceRetirementPhase.SealBarrier,
            ResourceRetirementAttemptPurpose.OwnerReservation => ResourceRetirementPhase.ReserveOwner,
            ResourceRetirementAttemptPurpose.CleanupPage => ResourceRetirementPhase.CleanupPage,
            _ => throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed)
        };
}
