namespace KeyLoad.Clustering;

internal static class AnnProjectionLifetimeObservationScope
{
    internal const string RequestAlias = "keyload.ann-projection-observation.request.v2";
    internal const string Purpose = "keyload-ann-projection-observation-v2";

    internal static bool Handles(GrainReadKind? kind) => kind == GrainReadKind.AnnProjectionObservationV2;

    internal static bool Validate(GrainRequestEnvelope envelope)
    {
        if (!Handles(envelope.ReadKind) && envelope.Purpose != Purpose)
        {
            return false;
        }
        if (!Handles(envelope.ReadKind) || envelope.Purpose != Purpose
            || envelope.CommandKind is not null || envelope.CommandId != Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return true;
    }
}
