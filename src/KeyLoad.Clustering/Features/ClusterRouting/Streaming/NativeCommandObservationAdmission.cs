using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal static class NativeCommandObservationAdmission
{
    internal static async Task<NativeCommandObservation> ReserveAsync(NativeRequestWorkOwner owner,
        DecodedGrainRequest verified, NativeCommandObservationAdmissionV1 admission, NativeCapabilityWorkLifetime commandWork, PhysicalShardRecord actualOwner, ReplicaSiloDiscovery actualRuntime,
        DatabaseEngine database, TimeProvider clock, CancellationToken originalCallerToken)
    {
        NativeCommandObservationIdentity.RequireAdmission(verified, admission, database);
        var resources = new NativeCommandObservationResources(database.OperationLimitsOptions, admission.MaximumResultBytes);
        NativeRequestWorkLease? lease = null;
        NativeCommandObservation? observation = null;
        Exception? primary = null;
        try
        {
            lease = owner.AcquireCommandObservation(admission.ObservationId, commandWork.OriginalProducer,
                actualOwner, actualRuntime, database, resources);
            observation = new(owner, lease, resources, database, commandWork.OriginalProducer);
            lease.CommandObservation = observation;
            observation.Initialize(verified, admission, actualOwner, actualRuntime, clock, originalCallerToken);
            return observation;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            primary = error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            primary = error;
        }
        Exception? cleanup;
        if (observation is not null)
        {
            observation.FinishPreparation();
            var abort = observation.AbortAsync(primary);
            observation.FinishInitialization();
            cleanup = await NativeRequestWorkSettlement.JoinAsync(abort).ConfigureAwait(false);
        }
        else
        {
            cleanup = await resources.DisposeAsync().ConfigureAwait(false);
            if (lease is not null)
            {
                var outcome = await lease.CompleteAsync(primary, cleanup).ConfigureAwait(false);
                cleanup = outcome.CleanupOrJoinFailure;
            }
        }
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(primary, cleanup));
        throw Errors.Fail(ErrorCode.Corruption, NativeRequestWorkLimits.LeaseIdentityMessage);
    }
}
