using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal object? ReadControlledBlob(string localPrincipalId, ControlledBlobReadFrame frame,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant, CancellationToken cancellationToken)
    {
        work.Check();
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            RequireControlledBlobReceiver(charged, localPrincipalId, frame);
            var blob = BlobControlledReadScope.Require(frame);
            var controlled = new PartitionControlResourceView(charged, blob.Partition,
                [frame.Resource], Limits.MaxBatchBytes, work, grant);
            if (frame.Purpose == ControlledBlobReadPurpose.Outcome)
            {
                AuthorizeOperation(controlled, frame.Principal, frame.Original!);
                ValidateCachedResult(controlled, frame.Principal, frame.Original!, frame.OriginalOutcome!);
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return ReadControlledBlobData(controlled, frame);
        });
        work.MeasureResult(result);
        work.Check();
        return result;
    }

    private object? ReadControlledBlobData(IKeyValueView view, ControlledBlobReadFrame frame)
    {
        var reads = new BlobReads(this, EvaluationClock);
        return frame.Purpose switch
        {
            ControlledBlobReadPurpose.Metadata => reads.Metadata(view, frame.Principal,
                NativeSerialization.Deserialize<BlobMetadataRequest>(frame.NativeRequest.Span)),
            ControlledBlobReadPurpose.UploadInfo => reads.UploadInfo(view, frame.Principal,
                NativeSerialization.Deserialize<BlobUploadInfoRequest>(frame.NativeRequest.Span)),
            ControlledBlobReadPurpose.Range => reads.Range(view, frame.Principal,
                NativeSerialization.Deserialize<BlobReadRequest>(frame.NativeRequest.Span)),
            ControlledBlobReadPurpose.List => reads.List(view, frame.Principal,
                NativeSerialization.Deserialize<BlobListRequest>(frame.NativeRequest.Span)),
            _ => throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid)
        };
    }
}
