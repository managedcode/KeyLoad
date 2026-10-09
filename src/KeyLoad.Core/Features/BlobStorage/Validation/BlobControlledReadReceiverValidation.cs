using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireControlledBlobReceiver(IKeyValueView view, string localPrincipalId,
        ControlledBlobReadFrame frame)
    {
        var now = EvaluationClock.GetUtcNow();
        var local = Principal(view, localPrincipalId, now);
        if (!local.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        if (frame is null || frame.Principal is null || frame.Resource is null
            || frame.Control is null || frame.Publication is null)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        var blob = BlobControlledReadScope.Require(frame);
        ValidatePartition(blob.Partition);
        var stage = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(view,
            PartitionMoveTargetStorage.Key(blob.Partition), Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        var publication = PartitionMovePublishedPlacementStorage.Read(view, blob.Partition)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (frame.Version != PartitionMoveProtocol.Version || frame.QueryId == Guid.Empty
            || frame.ExpiresAt <= now || frame.Principal.Revoked || frame.Principal.ExpiresAt <= now
            || frame.Principal.PolicyEpoch <= PartitionMoveProtocol.EmptyCount || !stage.Published
            || string.IsNullOrWhiteSpace(frame.Principal.Id) || frame.DirectoryRevision <= PartitionMoveProtocol.EmptyCount
            || frame.Control.Phase != PartitionMovePhase.Retired || frame.Control.MoveId != stage.Control.MoveId
            || blob.Partition != frame.Control.Partition
            || PartitionMoveIntentIdentity.Digest(frame.Control) != PartitionMoveIntentIdentity.Digest(stage.Control)
            || JsonData.Fingerprint(frame.Publication) != JsonData.Fingerprint(publication)
            || frame.Publication.Destination.Incarnation != Store.Identity.Incarnation
            || frame.Resource.Name != blob.Resource || frame.Resource.Kind != ResourceKind.BlobStore
            || frame.Resource.TransactionDomainId != blob.Partition.TransactionDomainId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var baseline = stage.Descriptor.Resources.SingleOrDefault(value => value.Name == frame.Resource.Name);
        if (baseline is null || !ResourcePolicyUpdates.SameNonPolicyDefinition(baseline, frame.Resource))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMoveProtocol.InvalidImage); }
        BlobQuotaOperations.ValidatePolicy(frame.Resource);
        RequireControlledBlobReadPolicy(frame, blob);
    }

    private void RequireControlledBlobReadPolicy(ControlledBlobReadFrame frame, BlobRef blob)
    {
        if (frame.Purpose != ControlledBlobReadPurpose.Outcome)
        {
            ClusterPrincipalPolicy.RequireOperation(frame.Principal, OperationKind.BeginBlobUpload);
            Authorization.Require(frame.Principal, blob.Partition, blob.Resource,
                BlobControlledReadScope.Capability(frame.Purpose));
            return;
        }
        var original = frame.Original!;
        var previous = frame.OriginalOutcome!;
        if (original.PrincipalId != frame.Principal.Id || previous.ScopeKind != CommandOutcomeScopeKind.Partition
            || previous.Partition != blob.Partition
            || previous.Incarnation != frame.Publication.ControlFinalize.PhysicalOwner.Incarnation
            || previous.Fingerprint != CommandFingerprint(original))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        if (previous.PolicyEpoch != frame.Principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage); }
        _ = RequireControlledBlobPolicies(frame.Principal, original, [frame.Resource]);
    }
}
