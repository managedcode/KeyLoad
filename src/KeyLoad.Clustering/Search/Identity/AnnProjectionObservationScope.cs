namespace KeyLoad.Clustering;

internal static class AnnProjectionObservationScope
{
    internal static bool SamePhysicalIdentity(PhysicalShardRecord host, PhysicalShardRecord original)
        => host.PhysicalShardId == original.PhysicalShardId && host.Incarnation == original.Incarnation
            && host.VoterIds.SequenceEqual(original.VoterIds, StringComparer.Ordinal);

    internal static bool Handles(GrainReadKind? kind) => kind == GrainReadKind.AnnProjectionObservation;

    internal static bool Validate(GrainRequestEnvelope envelope)
    {
        if (!Handles(envelope.ReadKind) && envelope.Purpose != AnnProjectionObservationProtocol.Purpose)
        {
            return false;
        }
        if (envelope.Purpose != AnnProjectionObservationProtocol.Purpose
            || !Handles(envelope.ReadKind) || envelope.CommandKind is not null
            || envelope.CommandId != Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return true;
    }
}
