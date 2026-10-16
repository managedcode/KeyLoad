using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Attaches bounded observation authority to the original verified command bytes.</summary>
internal static class NativeCommandObservationRequestIssuance
{
    private const int NoReadBytes = 0;
    private const int NoExaminedRecords = 0;
    private const int NoResultBytes = 0;

    internal static string Attach(GrainRequestCodec codec, DatabaseEngine database,
        DecodedGrainRequest request, NativeCommandObservationAdmissionV1 admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (!NativeCommandObservationScope.IsEligible(request)
            || request.Envelope.Observation is not null
            || !request.Payload.Span.SequenceEqual(request.Envelope.Payload.Span)
            || admission.Version != NativeCommandObservationProtocol.CurrentVersion
            || admission.ObservationId == Guid.Empty
            || admission.MaximumReadBytes <= NoReadBytes || admission.MaximumReadBytes > database.Limits.MaxQueryReadBytes
            || admission.MaximumExaminedRecords <= NoExaminedRecords || admission.MaximumExaminedRecords > database.Limits.MaxScanRecords
            || admission.MaximumResultBytes <= NoResultBytes || admission.MaximumResultBytes > database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        // Issue preserves the verified operation identity, original expiration and exact original payload.
        return codec.Issue(request.Envelope with
        {
            Observation = admission
        });
    }
}
