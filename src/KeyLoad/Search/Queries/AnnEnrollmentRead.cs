using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal static class AnnEnrollmentRead
{
    private const int CaptureFrames = 2;

    internal static async Task<(bool HasValue, AnnProjectionEnrollment? Value)> ReadAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceRequest original, ServerRuntimeOptions configured,
        TimeProvider clock, AnnMaintenanceAdmissions admissions,
        Func<AnnGenerationOwner> owningRoot, CancellationToken token,
        ReadExecutionBudget? originalBudget = null, IResourceLifetimeWorkBinding? binding = null)
    {
        var failures = new List<Exception>();
        AnnProjectionEnrollment? result = null;
        try
        {
            using var operation = admissions.Enter();
            try
            {
                AnnMaintenanceAdmission.Request(original, original.CommandId, database.Store.Identity.NodeId);
                var read = originalBudget ?? new ReadExecutionBudget(clock, token);
                var eligible = binding is null
                    ? database.ReadProjectionMaintenanceEligibility(original.Consumer, original.IndexGeneration, read)
                    : database.ReadProjectionMaintenanceEligibilityBound(original.Consumer, original.IndexGeneration, read, binding);
                RequireReservation(eligible, principal, original);
                if (!eligible.InitialAnnMaintenanceSettled)
                {
                    return (false, default(AnnProjectionEnrollment?));
                }
                var owner = owningRoot();
                result = await owner.RunAsync(stages => (ReadOwned(database, principal, original,
                    configured, clock, owner, stages, eligible, read, binding)).Value);
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return ((AnnProjectionEnrollment?)(result)) is { } outcomeValue2 ? (true, outcomeValue2) : (false, default(AnnProjectionEnrollment?));
    }

    private static (bool HasValue, AnnProjectionEnrollment? Value) ReadOwned(DatabaseEngine database, PrincipalRecord principal,
        AnnMaintenanceRequest original, ServerRuntimeOptions configured, TimeProvider clock,
        AnnGenerationOwner owner, AnnStageStore stages, ProjectionConsumerInfo eligible,
        ReadExecutionBudget read, IResourceLifetimeWorkBinding? binding)
    {
        var failures = new List<Exception>();
        AnnProjectionEnrollment? result = null;
        try
        {
            using var memory = owner.MaintenanceMemory.Reserve();
            try
            {
                var candidate = (stages.ReadEnrollment(original)).Value;
                if (candidate is null)
                {
                    read.Check();
                    return (false, default(AnnProjectionEnrollment?));
                }
                var fresh = database.Store.Read(view => database.Principal(read.CreateView(view), principal.Id, clock.GetUtcNow()));
                RequireReservation(eligible, fresh, original);
                var seeds = AnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, memory.Bytes / CaptureFrames);
                var captured = AnnSeedCollector.CapturePinned(database, fresh.Id, candidate.LastRequest, seeds, read);
                var budget = new AnnWorkBudget(read, seeds.Value.MaxWorkUnits);
                var published = stages.ReadPublished(candidate.LastRequest, captured, seeds.Value,
                    owner.MaintenanceMemory.Remaining(captured.OwnedBytesUpperBound), budget, read);
                AnnOriginalResults.RequireResult(candidate.LastRequest, candidate.LastResult);
                AnnOriginalResults.RequirePublication(candidate.LastResult, published, captured);
                if (published.Count != candidate.LastResult.Count
                    || Convert.ToHexStringLower(published.IndexSha256) != candidate.LastResult.IndexSha256)
                {
                    throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
                }
                var current = binding is null
                    ? database.ReadProjectionMaintenanceEligibility(original.Consumer, original.IndexGeneration, read)
                    : database.ReadProjectionMaintenanceEligibilityBound(original.Consumer, original.IndexGeneration, read, binding);
                RequireReservation(current, fresh, original);
                if (current.MaintenanceAuthority != eligible.MaintenanceAuthority
                    || current.Checkpoint != eligible.Checkpoint)
                {
                    throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
                }
                result = candidate;
                read.Check();
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return ((AnnProjectionEnrollment?)(result)) is { } outcomeValue2 ? (true, outcomeValue2) : (false, default(AnnProjectionEnrollment?));
    }

    private static void RequireReservation(ProjectionConsumerInfo state, PrincipalRecord principal,
        AnnMaintenanceRequest original)
    {
        if (state.InitialAnnMaintenance is not { } reserved || state.MaintenanceAuthority is not { } authority
            || authority.PrincipalId != principal.Id || authority.PolicyEpoch != principal.PolicyEpoch
            || principal.Revoked || !principal.ClusterAdministrator || state.Released
            || !AnnOriginalResults.SameRequest(reserved, original))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
        }
    }
}
