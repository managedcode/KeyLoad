using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ControlledBlobReadFrameV2? TryCaptureControlledBlobOutcomeV2(string principalId,
        ReplicatedOperation operation, DateTimeOffset requestExpiry, ReadExecutionBudget work)
    {
        work.Check();
        var original = VerifyOperationAuthority(operation);
        if (original.PrincipalId != principalId)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ClusterAdministrationRequiredMessage);
        }
        var frame = Store.Read(view => CaptureControlledBlobOutcomeViewV2(work.CreateView(view),
            principalId, original, requestExpiry, Guid.NewGuid()));
        work.MeasureResult(frame);
        work.Check();
        return frame;
    }

    private ControlledBlobReadFrameV2? CaptureControlledBlobOutcomeViewV2(IKeyValueView view,
        string principalId, ReplicatedOperation operation, DateTimeOffset expiry, Guid queryId)
    {
        var original = CaptureControlledBlobOutcomeView(view, principalId, operation, expiry, queryId);
        if (original is null)
        {
            return null;
        }
        var blob = BlobControlledReadScope.Require(original);
        var coverage = PartitionControlLifetimeCoverage.Capture(view, blob.Partition,
            [original.Resource], Limits.MaxBatchBytes);
        var frame = new ControlledBlobReadFrameV2(original, coverage);
        if (NativeSerialization.Measure(frame) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        return frame;
    }

    internal void ValidateControlledBlobOutcomeV2(ControlledBlobReadFrameV2 original, ReadExecutionBudget work)
    {
        work.Check();
        var frame = original.Original;
        var current = Store.Read(view => CaptureControlledBlobOutcomeViewV2(work.CreateView(view),
            frame.Principal.Id, frame.Original!, frame.ExpiresAt, frame.QueryId));
        if (current is null || !NativeSerialization.Serialize(current).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(original)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        }
        work.Check();
    }
}
