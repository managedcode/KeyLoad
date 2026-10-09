using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static partial class PartitionMovementParentNativePhases
{
    internal static PartitionMovePhaseCommand Prepare(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state)
    {
        if (request.Mode != PartitionMoveMode.Transfer || state.Header is not null || state.Pending is not null
            || state.Placement.Revision != request.ExpectedPlacementRevision)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var destination = state.Directory.Owners.SingleOrDefault(
            entry => entry.Owner.PhysicalShardId == request.DestinationPhysicalShardId)?.Owner
            ?? throw Errors.Fail(ErrorCode.NotFound, PartitionMoveProtocol.OwnerMismatch);
        var bytes = NativeSerialization.Serialize(new PartitionMovePrepareBody(principalId, request,
            RequireParentCheckpoint: true));
        return new(PartitionMoveProtocol.Version, request.MoveId, request.Partition,
            state.Directory.ControlOwner, state.Placement, destination,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), PartitionMovePeerStage.ControlPrepare,
            PartitionMoveProtocol.EmptyCount, bytes, Resources: ImmutableArray<ResourceDefinition>.Empty);
    }

    internal static PartitionMovePhaseCommand BeginAbort(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control)
        => Frame(header, control, PartitionMovePeerStage.ControlBeginAbort,
            NativeSerialization.Serialize(new PartitionMoveControlBody(header.OperatorPrincipalId, control)));

    internal static PartitionMovePhaseCommand Fence(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control)
        => Frame(header, control, PartitionMovePeerStage.Fence,
            NativeSerialization.Serialize(new PartitionMoveControlBody(header.OperatorPrincipalId, control)));

    internal static PartitionMovePhaseCommand Authorize(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, Guid grantId, Guid effectId, PartitionMovePhaseCommand effect,
        PhysicalShardRecord receiver, DateTimeOffset expiresAt)
        => Frame(header, control, PartitionMovePeerStage.ControlAuthorize,
            NativeSerialization.Serialize(new PartitionMoveAuthorizeBody(grantId, effectId,
                header.OperatorPrincipalId, effect, receiver, expiresAt)), effect.PageOrdinal);

    internal static PartitionMovePhaseCommand AttachActualGrant(PartitionMovePhaseCommand intended,
        Guid effectId, PhysicalShardRecord receiver, DateTimeOffset originalExpiry, PartitionMovePhaseResult authorization)
    {
        var grant = authorization.Grant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var digest = Convert.ToHexStringLower(SHA256.HashData(intended.Body.Span));
        if (authorization.Stage != PartitionMovePeerStage.ControlAuthorize
            || authorization.MoveId != intended.MoveId || authorization.Journal.CommandId != grant.GrantId
            || grant.PhaseCommandId != effectId || grant.MoveId != intended.MoveId
            || grant.Partition != intended.Partition || grant.Stage != intended.Stage
            || grant.PageOrdinal != intended.PageOrdinal || grant.BodyDigest != digest
            || grant.ControlIntentDigest != intended.ControlIntentDigest || grant.ExpiresAt != originalExpiry
            || grant.Settlement is not null || grant.AbortDisposition is not null
            || grant.AdmissionPosition != authorization.Journal.AppliedPosition
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, receiver)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ControlOwner, intended.ControlOwner))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        return intended with { GrantId = grant.GrantId, Resources = grant.Resources };
    }

    internal static PartitionMovePhaseCommand Acknowledge(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, PartitionMovePhaseGrant originalGrant, PartitionMovePhaseResult actualEffect)
        => Frame(header, control, PartitionMovePeerStage.ControlAcknowledge,
            NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(originalGrant.GrantId, actualEffect.Journal)),
            originalGrant.PageOrdinal);

    internal static PartitionMovePhaseCommand AcceptFence(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, Guid originalFenceGrantId, PartitionMoveSourceFenceRecord actualFence)
        => Frame(header, control, PartitionMovePeerStage.ControlAcceptFence,
            NativeSerialization.Serialize(new PartitionMoveFenceAcceptBody(header.OperatorPrincipalId,
                control, actualFence, originalFenceGrantId)));

    internal static PartitionMovePhaseCommand Install(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, PartitionMoveSourceFenceRecord originalFence,
        PartitionMoveImageDescriptor originalDescriptor, int actualNextOrdinal)
    {
        var total = originalDescriptor.Families.Sum(static family => family.PageCount);
        if (actualNextOrdinal < PartitionMoveProtocol.EmptyCount || actualNextOrdinal > total)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        return Frame(header, control, PartitionMovePeerStage.Install,
            NativeSerialization.Serialize(new PartitionMoveInstallBody(header.OperatorPrincipalId,
                control, originalFence, originalDescriptor)), actualNextOrdinal);
    }

    internal static PartitionMovePhaseCommand StagePage(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, PartitionMoveSourceFenceRecord originalFence,
        PartitionMoveImageDescriptor originalDescriptor, PartitionMoveImagePage actualPage)
        => Frame(header, control, PartitionMovePeerStage.StagePage,
            NativeSerialization.Serialize(new PartitionMovePageBody(header.OperatorPrincipalId,
                control, originalFence, originalDescriptor, actualPage)), actualPage.Ordinal);

    private static PartitionMovePhaseCommand Frame(PartitionMoveParentHeader header,
        PartitionMoveControlRecord control, PartitionMovePeerStage stage, byte[] body,
        int ordinal = PartitionMoveProtocol.EmptyCount)
    {
        if (control.MoveId != header.MoveId || control.Partition != header.Partition
            || control.PrincipalId != header.OperatorPrincipalId || !PartitionMoveControlValidation.SameSource(control.SourcePlacement, header.SourcePlacement)
            || control.SourcePlacement.Version != header.SourcePlacement.Version
            || control.SourcePlacement.DirectoryRevision != header.SourcePlacement.DirectoryRevision
            || control.SourcePlacement.IsFallback != header.SourcePlacement.IsFallback
            || !PhysicalOwnerEntryValidation.SameOwner(control.DestinationOwner, header.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return new(PartitionMoveProtocol.Version, header.MoveId, header.Partition, header.ControlOwner,
            header.SourcePlacement, header.DestinationOwner, PartitionMoveIntentIdentity.Digest(control),
            stage, ordinal, body, Resources: ImmutableArray<ResourceDefinition>.Empty);
    }
}
