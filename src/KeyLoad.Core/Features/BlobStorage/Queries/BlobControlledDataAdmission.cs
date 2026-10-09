using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ControlledBlobReadFrame? TryCaptureControlledBlobRead(string principalId,
        ControlledBlobReadPurpose purpose, ReadOnlyMemory<byte> nativeRequest,
        DateTimeOffset expiry, ReadExecutionBudget work)
    {
        work.Check();
        var frame = Store.Read(view => CaptureControlledBlobReadView(work.CreateView(view), principalId,
            purpose, nativeRequest, expiry, Guid.NewGuid()));
        work.MeasureResult(frame);
        work.Check();
        return frame;
    }

    private ControlledBlobReadFrame? CaptureControlledBlobReadView(IKeyValueView view, string principalId,
        ControlledBlobReadPurpose purpose, ReadOnlyMemory<byte> nativeRequest, DateTimeOffset expiry, Guid queryId)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        ClusterPrincipalPolicy.RequireOperation(principal, OperationKind.BeginBlobUpload);
        var blob = BlobControlledReadScope.Require(purpose, null, null, nativeRequest);
        Authorization.Require(principal, blob.Partition, blob.Resource, BlobControlledReadScope.Capability(purpose));
        var publication = PartitionMovePublishedPlacementStorage.Read(view, blob.Partition);
        if (publication is null || publication.Destination.Incarnation == Store.Identity.Incarnation)
        { return null; }
        return CaptureControlledBlobFrame(view, principal, blob, purpose, null, null, nativeRequest, expiry, queryId);
    }

    internal void ValidateControlledBlobRead(ControlledBlobReadFrame original, ReadExecutionBudget work)
    {
        work.Check();
        var current = Store.Read(view => CaptureControlledBlobReadView(work.CreateView(view), original.Principal.Id,
            original.Purpose, original.NativeRequest, original.ExpiresAt, original.QueryId));
        if (current is null || JsonData.Fingerprint(current) != JsonData.Fingerprint(original))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        work.Check();
    }
}
