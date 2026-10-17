namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string AttachCommandObservation(DecodedGrainRequest request,
        NativeCommandObservationAdmissionV1 admission)
        => NativeCommandObservationRequestIssuance.Attach(this, database, request, admission);
}
