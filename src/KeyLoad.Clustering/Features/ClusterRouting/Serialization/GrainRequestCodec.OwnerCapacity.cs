using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateOwnerCapacityRead(Guid requestId, string verifiedPrincipalId,
        OwnerCapacityReadRequestV1 request)
        => Issue(GrainRequestEnvelopeConstruction.MovementRead(database, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, verifiedPrincipalId, GrainReadKind.OwnerCapacity,
            TenantPlacementCapacityProtocol.ReadPurpose, NativeSerialization.Serialize(request), request.OriginalExpiry));
}
