using KeyLoad.Clustering;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsAnnLifetimeReadProbe
{
    internal static bool MatchesCall(RemoteModelNativeRequest request, GrainReadKind? read, Guid parentRequestId)
    {
        if (request.Channel != RemoteModelNativeChannel.ResourceLifetimeAnnRead
            || read is not (GrainReadKind.WaitForAnnIndex or GrainReadKind.ApproximateSearch))
        {
            return false;
        }
        var call = Read(request, parentRequestId);
        return call.Original.Observation.Kind switch
        {
            AnnProjectionObservationKind.Wait => read == GrainReadKind.WaitForAnnIndex,
            AnnProjectionObservationKind.Approximate => read == GrainReadKind.ApproximateSearch,
            _ => false
        };
    }

    internal static void RequireReadWitness(RemoteModelNativeRequest request, Guid parentRequestId)
    {
        var call = Read(request, parentRequestId);
        if (call.Original.Observation.Kind is not (AnnProjectionObservationKind.Wait or AnnProjectionObservationKind.Approximate))
        {
            throw Invalid();
        }
    }

    private static AnnProjectionNativeCallV2 Read(RemoteModelNativeRequest request, Guid parentRequestId)
    {
        var call = NativeSerialization.Deserialize<AnnProjectionNativeCallV2>(request.Payload.Span);
        if (parentRequestId == Guid.Empty || call?.Original is not { } original
            || original.Version != AnnProjectionObservationProtocol.Version
            || original.Observation is not { OriginalRequest: not null } observation
            || observation.Version != AnnProjectionObservationProtocol.Version
            || !Enum.IsDefined(observation.Kind) || original.ExpiresAt != request.ExpiresAt
            || string.IsNullOrWhiteSpace(original.PrincipalId) || call.Principal is null
            || original.PrincipalId != call.Principal.Id || call.Definition is null || call.Coverage is null
            || call.Coverage.Lifetimes.IsDefaultOrEmpty || call.Definitions.IsDefaultOrEmpty
            || !call.Definitions.Any(resource => resource.Name == call.Definition.Name)
            || !AnnProjectionObservationScope.SamePhysicalIdentity(request.DestinationOwner, observation.OriginalRequest.Placement)
            || !ValidPayload(observation))
        {
            throw Invalid();
        }
        return call;
    }

    private static bool ValidPayload(AnnProjectionObservationRequest observation)
        => observation.Kind switch
        {
            AnnProjectionObservationKind.Identity => observation.Wait is null && observation.Approximate is null,
            AnnProjectionObservationKind.Wait => observation.Wait is not null && observation.Approximate is null,
            AnnProjectionObservationKind.Approximate => observation.Approximate is not null && observation.Wait is null,
            _ => false
        };

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
