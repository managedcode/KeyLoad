using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ControlledBlobReadFrameV2? TryCaptureControlledBlobReadV2(string principalId,
        ControlledBlobReadPurpose purpose, ReadOnlyMemory<byte> nativeRequest,
        DateTimeOffset expiry, ReadExecutionBudget work)
    {
        work.Check();
        var frame = Store.Read(view => CaptureControlledBlobReadViewV2(work.CreateView(view), principalId,
            purpose, nativeRequest, expiry, Guid.NewGuid()));
        work.MeasureResult(frame);
        work.Check();
        return frame;
    }

    private ControlledBlobReadFrameV2? CaptureControlledBlobReadViewV2(IKeyValueView view, string principalId,
        ControlledBlobReadPurpose purpose, ReadOnlyMemory<byte> nativeRequest, DateTimeOffset expiry, Guid queryId)
    {
        var original = CaptureControlledBlobReadView(view, principalId, purpose, nativeRequest, expiry, queryId);
        if (original is null)
        {
            return null;
        }
        var blob = BlobControlledReadScope.Require(original);
        var coverage = PartitionControlLifetimeCoverage.Capture(view, blob.Partition,
            [original.Resource], Limits.MaxBatchBytes);
        BindAuthenticatedReadResourceLifetime(view, blob.Partition, original.Resource, coverage.Lifetimes.Single());
        return new ControlledBlobReadFrameV2(original, coverage);
    }

    internal void ValidateControlledBlobReadV2(ControlledBlobReadFrameV2 original, ReadExecutionBudget work)
    {
        work.Check();
        var frame = original.Original;
        var current = Store.Read(view => CaptureControlledBlobReadViewV2(work.CreateView(view), frame.Principal.Id,
            frame.Purpose, frame.NativeRequest, frame.ExpiresAt, frame.QueryId));
        if (current is null || !NativeSerialization.Serialize(current).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(original)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        }
        work.Check();
    }
}
