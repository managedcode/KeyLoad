using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnEnrollmentRead
{
    private const int CaptureFrames = 2;

    internal static async Task<AnnProjectionEnrollment?> ReadAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceRequest original, ServerRuntimeOptions configured,
        TimeProvider clock, NativeAnnMaintenanceAdmissions admissions,
        Func<NativeAnnGenerationOwner> owningRoot, CancellationToken token)
    {
        var failures = new List<Exception>();
        AnnProjectionEnrollment? result = null;
        try
        {
            using var operation = admissions.Enter(configured.GrainRouting.Value.MaximumRequestProducers);
            try
            {
                NativeAnnMaintenanceAdmission.Request(original, original.CommandId, database.Store.Identity.NodeId);
                var read = new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, token);
                var eligible = database.ReadProjectionMaintenanceEligibility(original.Consumer,
                    original.IndexGeneration, read);
                RequireReservation(eligible, principal, original);
                if (!eligible.InitialAnnMaintenanceSettled)
                {
                    return null;
                }
                var owner = owningRoot();
                result = await owner.RunAsync(stages => ReadOwned(database, principal, original,
                    configured, clock, owner, stages, eligible, read)).ConfigureAwait(false);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static AnnProjectionEnrollment? ReadOwned(DatabaseEngine database, PrincipalRecord principal,
        AnnMaintenanceRequest original, ServerRuntimeOptions configured, TimeProvider clock,
        NativeAnnGenerationOwner owner, NativeAnnStageStore stages, ProjectionConsumerInfo eligible,
        ReadExecutionBudget read)
    {
        var failures = new List<Exception>();
        AnnProjectionEnrollment? result = null;
        try
        {
            using var memory = owner.MaintenanceMemory.Reserve();
            try
            {
                var candidate = stages.ReadEnrollment(original);
                if (candidate is null)
                {
                    read.Check();
                    return null;
                }
                var fresh = database.Store.Read(view => database.Principal(view, principal.Id, clock.GetUtcNow()));
                RequireReservation(eligible, fresh, original);
                var seeds = NativeAnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, memory.Bytes / CaptureFrames);
                var captured = AnnSeedCollector.CapturePinned(database, fresh.Id, candidate.LastRequest, seeds, read);
                var budget = new AnnWorkBudget(read, seeds.Value.MaxWorkUnits);
                var published = stages.ReadPublished(candidate.LastRequest, captured, seeds.Value,
                    owner.MaintenanceMemory.Remaining(captured.OwnedBytesUpperBound), budget, read);
                NativeAnnOriginalResults.RequireResult(candidate.LastRequest, candidate.LastResult);
                NativeAnnOriginalResults.RequirePublication(candidate.LastResult, published, captured);
                if (published.Count != candidate.LastResult.Count
                    || Convert.ToHexStringLower(published.IndexSha256) != candidate.LastResult.IndexSha256)
                {
                    throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt);
                }
                var current = database.ReadProjectionMaintenanceEligibility(original.Consumer,
                    original.IndexGeneration, read);
                RequireReservation(current, fresh, original);
                if (current.MaintenanceAuthority != eligible.MaintenanceAuthority
                    || current.Checkpoint != eligible.Checkpoint
                    || current.LastProgressReservationCut != eligible.LastProgressReservationCut)
                {
                    throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource);
                }
                result = candidate;
                read.ChargeBytes(global::KeyLoad.NativeSerialization.Measure(candidate));
                read.Check();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static void RequireReservation(ProjectionConsumerInfo state, PrincipalRecord principal,
        AnnMaintenanceRequest original)
    {
        if (state.InitialAnnMaintenance is not { } reserved || state.MaintenanceAuthority is not { } authority
            || authority.PrincipalId != principal.Id || authority.PolicyEpoch != principal.PolicyEpoch
            || principal.Revoked || !principal.ClusterAdministrator || state.Released
            || !NativeAnnOriginalResults.SameRequest(reserved, original))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource);
        }
    }
}
