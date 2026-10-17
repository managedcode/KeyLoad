using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Reserves actual target observation work before the one genuine ordinary submission.</summary>
internal static class NativeCommandObservationSubmission
{
    internal static Task<NativeCommandObservation?> ReserveAsync(DatabaseEngine database, TimeProvider clock,
        DecodedGrainRequest request, NativeRequestWorkOwner? owner, NativeCapabilityWorkLifetime? commandWork,
        IPhysicalRequestPlacement? physical, ReplicaSiloDiscoveryState? runtime, CancellationToken originalCallerToken)
    {
        if (request.Envelope.Observation is not { } admission)
        {
            return Task.FromResult<NativeCommandObservation?>(null);
        }
        return ReserveRequiredAsync(database, clock, request, admission, owner, commandWork,
            physical, runtime, originalCallerToken);
    }

    private static async Task<NativeCommandObservation?> ReserveRequiredAsync(DatabaseEngine database,
        TimeProvider clock, DecodedGrainRequest request, NativeCommandObservationAdmissionV1 admission,
        NativeRequestWorkOwner? owner, NativeCapabilityWorkLifetime? commandWork,
        IPhysicalRequestPlacement? physical, ReplicaSiloDiscoveryState? runtime, CancellationToken originalCallerToken)
    {
        if (!NativeCommandObservationScope.IsEligible(request) || owner is null || commandWork is null
            || physical is null || runtime is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return await owner.ReserveCommandObservationAsync(request, admission, commandWork,
            physical.Owner, runtime.Read(), database, clock, originalCallerToken).ConfigureAwait(true);
    }

    internal static async Task<Exception> AbortAsync(NativeCommandObservation? observation, Exception original)
    {
        if (observation is null)
        {
            return original;
        }
        try
        {
            await observation.AbortAsync(original).ConfigureAwait(true);
            return original;
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            return NativeRequestWorkSettlement.Preserve(original, cleanup)!;
        }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            return NativeRequestWorkSettlement.Preserve(original, cleanup)!;
        }
    }

}
