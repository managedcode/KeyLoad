using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireControlledBlobInitialPolicy(PrincipalRecord principal, ReplicatedOperation original)
    {
        ClusterPrincipalPolicy.RequireOperation(principal, original.Kind);
        ClusterPrincipalPolicy.RequireOperation(principal, OperationKind.BeginBlobUpload);
        var scope = BlobCommandScope.From(original);
        BlobKeys.Validate(scope.Blob);
        Authorization.Require(principal, scope.Blob.Partition, scope.Blob.Resource,
            BlobCommandScope.RequiredCapability(original.Kind));
    }

    private PartitionControlDocumentCommandContext CaptureControlledBlobCommandView(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation original, CommandOutcomePartitionScope scope,
        PartitionMovePublishedPlacement publication)
    {
        _ = RequireControlledCommandBody(original, scope.Partition!);
        _ = RequireControlledBlobPolicies(principal, original,
            PartitionMoveResources.Capture(view, scope.Partition!, Limits));
        var control = PartitionMoveControlStorage.ReadHistory(view, scope.Partition!, publication.MoveId,
            Limits.MaxBatchBytes) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var operatorPrincipal = Principal(view, control.PrincipalId, EvaluationClock.GetUtcNow());
        _ = RequireRetiredCommandControl(view, operatorPrincipal, control, out var placement);
        var identity = new PartitionControlCommandIdentity(scope.Kind, scope.Partition, original.PrincipalId, original.Id);
        var record = PartitionControlCommandStorage.Read(view, identity, Limits.MaxBatchBytes);
        if (record is not null && record.Fingerprint != CommandFingerprint(original))
        { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
        var selected = CommandOutcomeKeyResolver.Select(view, original.PrincipalId, original.Id, scope);
        OperationResult? retained = null;
        if (selected.Outcome is { } previous)
        {
            if (previous.PolicyEpoch != principal.PolicyEpoch)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage); }
            if (previous.Incarnation != Store.Identity.Incarnation)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage); }
            if (previous.Fingerprint != CommandFingerprint(original))
            { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
            CommandOutcomeKeyResolver.ValidateSelectedScope(view, original, selected);
            retained = previous.Result;
        }
        return new(control, placement, operatorPrincipal.Id, identity, record, retained);
    }
}
