using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ControlledBlobReadFrame? CaptureControlledBlobOutcomeView(IKeyValueView view,
        string principalId, ReplicatedOperation original, DateTimeOffset expiry, Guid queryId)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        global::KeyLoad.Core.Features.ClusterRouting.Identity.RuntimeJournalIdentity.RequireOperation(principal, original.Kind);
        global::KeyLoad.Core.Features.ClusterRouting.Identity.RuntimeJournalIdentity.RequireOperation(principal, OperationKind.BeginBlobUpload);
        var blob = BlobCommandScope.From(original).Blob;
        BlobKeys.Validate(blob);
        Authorization.Require(principal, blob.Partition, blob.Resource, BlobCommandScope.RequiredCapability(original.Kind));
        if (PartitionMovePublishedPlacementStorage.Read(view, blob.Partition) is null)
        {
            return null;
        }
        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(blob.Partition.TenantId,
            blob.Partition.DatabaseId, blob.Resource));
        _ = ResourceLifetimeCatalog.Read(view, blob.Partition.TenantId, blob.Partition.DatabaseId, blob.Resource, definition);
        var resources = PartitionMoveResources.Capture(view, blob.Partition, Limits);
        _ = RequireControlledBlobPolicies(principal, original, resources);
        var scope = CommandOutcomePartitionIdentity.Resolve(original);
        var selected = CommandOutcomeKeyResolver.Select(view, principalId, original.Id, scope);
        if (selected.Outcome is not { } previous)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
        }
        if (previous.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
        }
        if (previous.Incarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage);
        }
        if (previous.Fingerprint != CommandFingerprint(original))
        {
            throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage);
        }
        CommandOutcomeKeyResolver.ValidateSelectedScope(view, original, selected);
        return CaptureControlledBlobFrame(view, principal, blob, ControlledBlobReadPurpose.Outcome,
            original, previous, default, expiry, queryId);
    }
}
