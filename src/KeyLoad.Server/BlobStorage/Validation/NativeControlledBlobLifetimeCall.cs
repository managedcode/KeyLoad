using KeyLoad.Clustering;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeControlledBlobLifetimeCall
{
    internal static ControlledBlobReadRequestV2 Require(RemoteResourceLifetimeReadCallV2 call,
        int maximumBytes = RequestCqrsProbeProtocol.MaximumRecordBytes)
    {
        if (maximumBytes < RemoteResourceLifetimeReadProtocol.MinimumBytes
            || maximumBytes > RemoteDocumentProtocol.MaximumBodyBytes || call is null || call.Version != RemoteResourceLifetimeReadProtocol.Version
            || call.Kind != RemoteResourceLifetimeReadKindV2.ControlledBlob || call.Payload.IsEmpty
            || call.Payload.Length > maximumBytes)
        {
            throw Invalid();
        }
        var request = NativeSerialization.Deserialize<ControlledBlobReadRequestV2>(call.Payload.Span);
        var frame = request?.Frame?.Original;
        if (request is null || frame is null || frame.Purpose == ControlledBlobReadPurpose.Outcome
            || frame.ExpiresAt != call.ExpiresAt || frame.Principal is null || frame.Resource is null
            || frame.Control is null || frame.Publication is null || request.Frame.Coverage is null)
        {
            throw Invalid();
        }
        var blob = BlobControlledReadScope.Require(frame);
        if (blob.Partition != frame.Control.Partition)
        {
            throw Invalid();
        }
        PartitionControlLifetimeCoverage.Require(request.Frame.Coverage, blob.Partition,
            [frame.Resource], maximumBytes);
        return request;
    }

    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
