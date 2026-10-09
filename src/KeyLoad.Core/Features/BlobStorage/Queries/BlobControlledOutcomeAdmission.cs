using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ControlledBlobReadFrame? TryCaptureControlledBlobOutcome(string principalId,
        ReplicatedOperation operation, DateTimeOffset requestExpiry, ReadExecutionBudget work)
    {
        work.Check();
        var original = VerifyOperationAuthority(operation);
        if (original.PrincipalId != principalId)
        { throw Errors.Fail(ErrorCode.Unauthenticated, ClusterAdministrationRequiredMessage); }
        var frame = Store.Read(view => CaptureControlledBlobOutcomeView(work.CreateView(view),
            principalId, original, requestExpiry, Guid.NewGuid()));
        work.MeasureResult(frame);
        work.Check();
        return frame;
    }

    private ControlledBlobReadFrame? CaptureControlledBlobOutcomeView(IKeyValueView view,
        string principalId, ReplicatedOperation original, DateTimeOffset expiry, Guid queryId)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        ClusterPrincipalPolicy.RequireOperation(principal, original.Kind);
        ClusterPrincipalPolicy.RequireOperation(principal, OperationKind.BeginBlobUpload);
        var blob = BlobCommandScope.From(original).Blob;
        BlobKeys.Validate(blob);
        Authorization.Require(principal, blob.Partition, blob.Resource, BlobCommandScope.RequiredCapability(original.Kind));
        if (PartitionMovePublishedPlacementStorage.Read(view, blob.Partition) is null)
        { return null; }
        var resources = PartitionMoveResources.Capture(view, blob.Partition, Limits);
        _ = RequireControlledBlobPolicies(principal, original, resources);
        var scope = CommandOutcomePartitionIdentity.Resolve(original);
        var selected = CommandOutcomeKeyResolver.Select(view, principalId, original.Id, scope);
        if (selected.Outcome is not { } previous)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage); }
        if (previous.PolicyEpoch != principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage); }
        if (previous.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage); }
        if (previous.Fingerprint != CommandFingerprint(original))
        { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
        CommandOutcomeKeyResolver.ValidateSelectedScope(view, original, selected);
        return CaptureControlledBlobFrame(view, principal, blob, ControlledBlobReadPurpose.Outcome,
            original, previous, default, expiry, queryId);
    }

    internal void ValidateControlledBlobOutcome(ControlledBlobReadFrame original, ReadExecutionBudget work)
    {
        work.Check();
        var current = Store.Read(view => CaptureControlledBlobOutcomeView(work.CreateView(view),
            original.Principal.Id, original.Original!, original.ExpiresAt, original.QueryId));
        if (current is null || JsonData.Fingerprint(current) != JsonData.Fingerprint(original))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        work.Check();
    }
}
