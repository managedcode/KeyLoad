using KeyLoad.Core.Features.ClusterRouting.Authorization;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MovementFirstAuthorityRecord = 1;
    private static readonly byte[][] MovementAuthorityPrefixes =
    [
        KeyCodec.Encode(PartitionMoveProtocol.SourceFenceSpace),
        KeyCodec.Encode(PartitionMoveProtocol.TargetStageSpace),
        KeyCodec.Encode(PartitionMoveProtocol.TargetPageSpace),
        KeyCodec.Encode(PartitionMoveProtocol.ActiveSpace),
        KeyCodec.Encode(PartitionMoveProtocol.RecordSpace),
        KeyCodec.Encode(PartitionMoveProtocol.GrantSpace),
        KeyCodec.Encode(PartitionMoveProtocol.GrantMoveIndexSpace),
        KeyCodec.Encode(PartitionMoveProtocol.MoveGrantCountSpace),
        KeyCodec.Encode(PartitionMoveProtocol.DatabaseGrantSpace),
        KeyCodec.Encode(PartitionMoveProtocol.PrincipalGrantSpace),
        KeyCodec.Encode(PartitionMoveProtocol.CleanupSpace),
        KeyCodec.Encode(PartitionMoveProtocol.AbortProgressSpace),
        KeyCodec.Encode(PartitionMoveProtocol.CommandAuthoritySpace),
        KeyCodec.Encode(PartitionMovePublishedPlacementStorage.Space),
    ];

    private void RequireConfiguredMovementOwner()
    {
        if (configuredPhysicalOwner is null)
        {
            throw PartitionMoveOwnerDiagnostic.Fail(PartitionMoveOwnerGuard.ConfiguredReceiver);
        }
    }

    /// <summary>Proves movement-authority absence from the original gated view without consulting or publishing the live cache.</summary>
    internal bool CaptureUnconfiguredMovementAbsence(IKeyValueView original, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        if (configuredPhysicalOwner is not null)
        {
            return false;
        }
        foreach (var prefix in MovementAuthorityPrefixes)
        {
            budget.VisitRange(original, prefix, MovementFirstAuthorityRecord, static (_, _) =>
                throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Fenced));
        }
        budget.Check();
        return true;
    }

    private void RequireUnconfiguredMovementAbsent(IKeyValueView view)
    {
        if (view is IPartitionMovementReadScope { MovementAuthorityAbsent: true })
        {
            return;
        }
        var scope = view as IPartitionMovementReadScope;
        var canReuse = scope?.CanReuseCommittedMovementAbsence ?? view is not IAtomicTransaction;
        var canPublish = scope?.CanPublishCommittedMovementAbsence ?? view is not IAtomicTransaction;
        var identity = Store.Identity;
        var cut = new MovementAuthorityCut(identity.NodeId, identity.Incarnation, identity.ReadGeneration, Store.Position);
        if (canReuse && HasMovementAbsence(cut))
        {
            scope?.MovementAuthorityAbsent = true;
            return;
        }
        foreach (var prefix in MovementAuthorityPrefixes)
        {
            view.VisitRange(prefix, MovementFirstAuthorityRecord, static (_, _) =>
                throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Fenced));
        }
        scope?.MovementAuthorityAbsent = true;
        if (canPublish)
        {
            PublishMovementAbsence(cut);
        }
    }

    private void ValidateMovementRestoration()
    {
        if (configuredPhysicalOwner is null)
        {
            Store.Read(view =>
            {
                RequireUnconfiguredMovementAbsent(view);
                return true;
            });
        }
    }
}
