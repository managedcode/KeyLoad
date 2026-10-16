using KeyLoad.Core;

namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string AttachCommandObservation(DecodedGrainRequest request,
        NativeCommandObservationAdmissionV1 admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (!NativeCommandObservationScope.IsEligible(request)
            || request.Envelope.Observation is not null
            || !request.Payload.Span.SequenceEqual(request.Envelope.Payload.Span)
            || admission.Version != NativeCommandObservationProtocol.CurrentVersion
            || admission.ObservationId == Guid.Empty
            || admission.MaximumReadBytes <= 0 || admission.MaximumReadBytes > database.Limits.MaxQueryReadBytes
            || admission.MaximumExaminedRecords <= 0 || admission.MaximumExaminedRecords > database.Limits.MaxScanRecords
            || admission.MaximumResultBytes <= 0 || admission.MaximumResultBytes > database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        // Issue preserves the verified operation identity, original expiration and exact original payload.
        return Issue(request.Envelope with
        {
            Observation = admission
        });
    }
}
