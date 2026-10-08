using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal DocumentResult? ReadControlledDocument(string localPrincipalId, PartitionControlDocumentReadFrame frame,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant, CancellationToken cancellationToken)
    {
        work.Check();
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            RequireControlledDocumentReadReceiver(charged, localPrincipalId, frame);
            var controlled = new PartitionControlResourceView(charged, frame.Reference.Partition,
                [frame.Resource], Limits.MaxBatchBytes, work, grant);
            return ReadDocumentAtCut(controlled, frame.Principal, frame.Reference, frame.MinimumToken, cancellationToken);
        });
        work.MeasureResult(result);
        work.Check();
        return result;
    }

    private void RequireControlledDocumentReadReceiver(IKeyValueView view, string localPrincipalId,
        PartitionControlDocumentReadFrame frame)
    {
        if (frame is null || frame.Reference is null || frame.Reference.Partition is null
            || frame.Principal is null || frame.Resource is null || frame.Control is null || frame.Publication is null)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        ValidatePartition(frame.Reference.Partition);
        JsonData.Identifier(frame.Reference.Collection);
        JsonData.Identifier(frame.Reference.Id);
        var now = EvaluationClock.GetUtcNow();
        var local = Principal(view, localPrincipalId, now);
        var stage = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(view,
            PartitionMoveTargetStorage.Key(frame.Reference.Partition), Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        var publication = PartitionMovePublishedPlacementStorage.Read(view, frame.Reference.Partition)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (!local.ClusterAdministrator || frame.Version != PartitionMoveProtocol.Version || frame.QueryId == Guid.Empty
            || frame.ExpiresAt <= now || frame.Principal.Revoked || frame.Principal.ExpiresAt <= now
            || frame.Principal.PolicyEpoch <= PartitionMoveProtocol.EmptyCount || !stage.Published
            || string.IsNullOrWhiteSpace(frame.Principal.Id) || frame.DirectoryRevision <= PartitionMoveProtocol.EmptyCount
            || frame.Control.Phase != PartitionMovePhase.Retired || frame.Control.MoveId != stage.Control.MoveId
            || frame.Reference.Partition != frame.Control.Partition
            || PartitionMoveIntentIdentity.Digest(frame.Control) != PartitionMoveIntentIdentity.Digest(stage.Control)
            || JsonData.Fingerprint(frame.Publication) != JsonData.Fingerprint(publication)
            || frame.Publication.Destination.Incarnation != Store.Identity.Incarnation
            || frame.Resource.Name != frame.Reference.Collection)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var baseline = stage.Descriptor.Resources.SingleOrDefault(value => value.Name == frame.Resource.Name);
        if (baseline is null || !ResourcePolicyUpdates.SameNonPolicyDefinition(baseline, frame.Resource))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMoveProtocol.InvalidImage); }
        if (!frame.Principal.ClusterAdministrator && frame.Principal.TenantId != frame.Reference.Partition.TenantId)
        { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteDocumentTenantDenied); }
        Authorization.Require(frame.Principal, frame.Reference.Partition, frame.Reference.Collection, Capability.DocumentsRead);
    }
}
