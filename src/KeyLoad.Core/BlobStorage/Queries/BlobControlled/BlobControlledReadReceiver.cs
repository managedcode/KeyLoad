using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
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
