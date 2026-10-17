using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks closed native terminal phase shape and identity before journal admission; apply revalidates actual state.</summary>
internal static class ResourceRetirementCompletionBodyValidation
{
    internal static Guid Require(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        ResourceRetirementValidation.Bound(phase, limits);
        return phase.Phase switch
        {
            ResourceRetirementPhase.CleanupPage => Cleanup(phase, limits),
            ResourceRetirementPhase.RecordOwnerCleanup => RecordCleanup(phase, limits),
            ResourceRetirementPhase.FinalizeCatalog => Source(phase,
                Read<ResourceRetirementFinalizeCatalogBodyV1>(phase).Control, limits),
            ResourceRetirementPhase.ReleaseOwnerTopology => Release(phase, limits),
            ResourceRetirementPhase.RecordOwnerRelease => RecordRelease(phase, limits),
            ResourceRetirementPhase.Finish => Source(phase, Read<ResourceRetirementFinishBodyV1>(phase).Control, limits),
            ResourceRetirementPhase.CompleteOwnerCleanup => CompleteCleanup(phase, limits),
            ResourceRetirementPhase.PersistDropParent => PersistParent(phase, limits),
            ResourceRetirementPhase.CompleteDropParent => CompleteParent(phase, limits),
            _ => throw Invalid()
        };
    }

    private static Guid Cleanup(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementCleanupPageBodyV1>(phase);
        RequireImmutableState(body.SealedState, limits);
        var identity = ResourceRetirementCleanupPageValidation.Require(body, limits);
        Parent(phase, body.SealedState.Control, limits);
        return identity;
    }

    private static Guid RecordCleanup(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementRecordOwnerCleanupBodyV1>(phase);
        Parent(phase, body.Control, limits);
        var cleanup = body.OwnerCleanup;
        if (cleanup is null || cleanup.Receiver is null || cleanup.Partitions.IsDefault
            || cleanup.Partitions.Length > limits.MaxResults
            || !ResourceRetirementValidation.Same(cleanup.Control, body.Control))
        {
            throw Invalid();
        }

        var member = ResourceRetirementStateValidation.Member(body.Control, cleanup.Receiver.Owner);
        if (!PhysicalOwnerEntryValidation.Same(member, cleanup.Receiver))
        {
            throw Invalid();
        }

        ResourceRetirementValidation.Receipt(cleanup.Receipt, body.Control,
            ResourceRetirementPhase.CompleteOwnerCleanup, member.Owner, limits);
        var seen = new HashSet<PartitionRef>();
        foreach (var checkpoint in cleanup.Partitions)
        {
            ResourceRetirementCleanupCheckpointValidation.Require(checkpoint, limits);
            if (!checkpoint.Complete || !seen.Add(checkpoint.Partition)
                || !ResourceRetirementValidation.Same(checkpoint.Control, body.Control)
                || !PhysicalOwnerEntryValidation.Same(checkpoint.Receiver, member)
                || checkpoint.Receipt.AppliedPosition >= cleanup.Receipt.AppliedPosition)
            {
                throw Invalid();
            }
        }
        return ResourceRetirementIdentity.PhaseCommandId(body.Control.Request, phase.Phase, member.Owner.PhysicalShardId);
    }

    private static Guid Release(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementReleaseOwnerTopologyBodyV1>(phase);
        ResourceRetirementFinalizationValidation.Require(body.Finalization, limits);
        var control = body.Finalization.Control;
        Parent(phase, control, limits);
        var receiver = body.Receiver;
        var member = ResourceRetirementStateValidation.Member(control, receiver?.Owner);
        if (receiver is null || !PhysicalOwnerEntryValidation.Same(member, receiver))
        {
            throw Invalid();
        }

        return ResourceRetirementIdentity.PhaseCommandId(control.Request, phase.Phase, member.Owner.PhysicalShardId);
    }

    private static Guid RecordRelease(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementRecordOwnerReleaseBodyV1>(phase);
        Parent(phase, body.Control, limits);
        ResourceRetirementCompletionValidation.Release(body.OwnerRelease, limits);
        if (!ResourceRetirementValidation.Same(body.OwnerRelease.Finalization.Control, body.Control))
        {
            throw Invalid();
        }

        return ResourceRetirementIdentity.PhaseCommandId(body.Control.Request, phase.Phase,
            body.OwnerRelease.Receiver.Owner.PhysicalShardId);
    }

    private static Guid CompleteCleanup(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementCompleteOwnerCleanupBodyV1>(phase);
        RequireImmutableState(body.SealedState, limits);
        var control = body.SealedState.Control;
        Parent(phase, control, limits);
        var receiver = body.Receiver;
        var member = ResourceRetirementStateValidation.Member(control, receiver?.Owner);
        if (receiver is null || !PhysicalOwnerEntryValidation.Same(member, receiver))
        {
            throw Invalid();
        }

        return ResourceRetirementIdentity.PhaseCommandId(control.Request, phase.Phase, member.Owner.PhysicalShardId);
    }

    private static void RequireImmutableState(ResourceRetirementStateV1 state, DatabaseLimits limits)
    {
        if (state is null || state.Stage != ResourceRetirementStage.BarrierSealed
            || state.OwnerCleanupReceipts.IsDefault || state.OwnerReleaseReceipts.IsDefault
            || !state.OwnerCleanupReceipts.IsEmpty || state.Finalization is not null
            || !state.OwnerReleaseReceipts.IsEmpty || state.CompletionReceipt is not null
            || state.CompletionOperation is not null || state.CompletionPolicyEpoch is not null)
        {
            throw Invalid();
        }

        ResourceRetirementWorkValidation.RequireBarrier(state, limits);
    }

    private static Guid PersistParent(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementPersistDropParentBodyV1>(phase);
        ResourceRetirementDropParentStorage.RequireLookup(body.Lookup, limits);
        ResourceRetirementValidation.Request(body.Expected, limits);
        if (phase.RetirementId != body.Lookup.CommandId || body.Expected.RetirementId != body.Lookup.CommandId
            || body.Expected.TenantId != body.Lookup.Request.TenantId
            || body.Expected.DatabaseId != body.Lookup.Request.DatabaseId
            || body.Expected.ResourceName != body.Lookup.Request.ResourceName)
        {
            throw Invalid();
        }

        return ResourceRetirementDropParentIdentity.CommandId(body.Lookup, phase.Phase);
    }

    private static Guid CompleteParent(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementCompleteDropParentBodyV1>(phase);
        ResourceRetirementDropParentStorage.RequireLookup(body.Lookup, limits);
        if (phase.RetirementId != body.Lookup.CommandId)
        {
            throw Invalid();
        }

        return ResourceRetirementDropParentIdentity.CommandId(body.Lookup, phase.Phase);
    }

    private static Guid Source(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementControlV1 control,
        DatabaseLimits limits)
    {
        Parent(phase, control, limits);
        return ResourceRetirementIdentity.PhaseCommandId(control.Request, phase.Phase, control.Catalog.DefaultShard.PhysicalShardId);
    }

    private static void Parent(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementControlV1 control,
        DatabaseLimits limits)
    {
        ResourceRetirementValidation.Control(control, limits);
        if (phase.RetirementId != control.Request.RetirementId)
        {
            throw Invalid();
        }
    }

    private static T Read<T>(ResourceRetirementPhaseCommandV1 phase) where T : class
        => NativeSerialization.Deserialize<T>(phase.Body.Span) ?? throw Invalid();

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
}
