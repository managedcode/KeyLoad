using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.BlobStorage;

internal static class BlobControlledReadScope
{
    internal static BlobRef Require(ControlledBlobReadFrame frame)
        => Require(frame.Purpose, frame.Original, frame.OriginalOutcome, frame.NativeRequest);

    internal static BlobRef Require(ControlledBlobReadPurpose purpose, ReplicatedOperation? original,
        StoredOutcome? outcome, ReadOnlyMemory<byte> nativeRequest)
    {
        if (!Enum.IsDefined(purpose))
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        if (purpose == ControlledBlobReadPurpose.Outcome)
        {
            if (original is null || outcome is null || !nativeRequest.IsEmpty
                || !BlobStorageOperations.Handles(original.Kind))
            { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
            var scope = BlobCommandScope.From(original);
            if (scope.CommandId != original.Id)
            { throw BlobErrors.Validation(); }
            BlobKeys.Validate(scope.Blob);
            return scope.Blob;
        }
        if (original is not null || outcome is not null || nativeRequest.IsEmpty)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        var blob = purpose switch
        {
            ControlledBlobReadPurpose.Metadata => NativeSerialization.Deserialize<BlobMetadataRequest>(nativeRequest.Span).Blob,
            ControlledBlobReadPurpose.UploadInfo => NativeSerialization.Deserialize<BlobUploadInfoRequest>(nativeRequest.Span).Blob,
            ControlledBlobReadPurpose.Range => NativeSerialization.Deserialize<BlobReadRequest>(nativeRequest.Span).Blob,
            ControlledBlobReadPurpose.List => ListScope(NativeSerialization.Deserialize<BlobListRequest>(nativeRequest.Span)),
            _ => throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid)
        };
        BlobKeys.Validate(blob);
        return blob;
    }

    internal static Capability Capability(ControlledBlobReadPurpose purpose)
        => purpose == ControlledBlobReadPurpose.UploadInfo ? global::KeyLoad.Capability.BlobWrite : global::KeyLoad.Capability.BlobRead;

    private static BlobRef ListScope(BlobListRequest request)
        => new(request.Partition, request.Resource, request.AfterId ?? BlobListAccumulator.ScopeId);
}
