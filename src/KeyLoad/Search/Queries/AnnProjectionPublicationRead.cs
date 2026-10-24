using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal static class AnnProjectionPublicationRead
{
    private const int RetainedFrames = 2;

    internal static async Task<AnnProjectionPublication> ReadAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceRequest request, ServerRuntimeOptions configured,
        TimeProvider clock, AnnMaintenanceAdmissions admissions,
        Func<AnnGenerationOwner> owningRoot, CancellationToken token,
        ReadExecutionBudget? originalBudget = null, IResourceLifetimeWorkBinding? binding = null)
    {
        var failures = new List<Exception>();
        AnnProjectionPublication? result = null;
        try
        {
            using var operation = admissions.Enter(configured.GrainRouting.Value.MaximumRequestProducers);
            try
            {
                result = await ReadOwnedAsync(database, principal, request, configured, clock, owningRoot, token, originalBudget, binding);
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
    }

    private static Task<AnnProjectionPublication> ReadOwnedAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceRequest request, ServerRuntimeOptions configured,
        TimeProvider clock, Func<AnnGenerationOwner> owningRoot, CancellationToken token,
        ReadExecutionBudget? originalBudget = null, IResourceLifetimeWorkBinding? binding = null)
    {
        AnnMaintenanceAdmission.Request(request, request.CommandId, database.Store.Identity.NodeId);
        var read = originalBudget ?? new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, token);
        var eligible = binding is null
            ? database.ReadProjectionMaintenanceEligibility(request.Consumer, request.IndexGeneration, read)
            : database.ReadProjectionMaintenanceEligibilityBound(request.Consumer, request.IndexGeneration, read, binding);
        RequireCaller(eligible, principal, request);
        var owner = owningRoot();
        return owner.RunAsync(stages => Read(database, principal, request, configured, owner,
            stages, eligible, read, binding));
    }

    private static AnnProjectionPublication Read(DatabaseEngine database, PrincipalRecord principal,
        AnnMaintenanceRequest request, ServerRuntimeOptions configured, AnnGenerationOwner owner,
        AnnStageStore stages, ProjectionConsumerInfo eligible, ReadExecutionBudget read,
        IResourceLifetimeWorkBinding? binding)
    {
        var failures = new List<Exception>();
        AnnProjectionPublication? result = null;
        try
        {
            using var memory = owner.MaintenanceMemory.Reserve();
            try
            {
                var seeds = AnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, memory.Bytes / RetainedFrames);
                var captured = AnnSeedCollector.CapturePinned(database, principal.Id, request, seeds, read);
                var remaining = owner.MaintenanceMemory.Remaining(captured.OwnedBytesUpperBound);
                var budget = new AnnWorkBudget(read, seeds.Value.MaxWorkUnits);
                var published = stages.ReadPublished(request, captured, seeds.Value, remaining, budget, read);
                var current = binding is null
                    ? database.ReadProjectionMaintenanceEligibility(request.Consumer, request.IndexGeneration, read)
                    : database.ReadProjectionMaintenanceEligibilityBound(request.Consumer, request.IndexGeneration, read, binding);
                RequireCaller(current, principal, request);
                if (current.MaintenanceAuthority != eligible.MaintenanceAuthority
                    || current.Checkpoint != eligible.Checkpoint
                    || current.LastProgressReservationCut != eligible.LastProgressReservationCut)
                {
                    throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
                }
                result = new(published.Consumer, published.IndexGeneration, published.Source, published.Count,
                    Convert.ToHexStringLower(published.IndexSha256));
                read.ChargeBytes(global::KeyLoad.OrleansSerialization.Measure(result));
                read.CheckResult(result);
                read.Check();
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
    }

    private static void RequireCaller(ProjectionConsumerInfo state, PrincipalRecord principal,
        AnnMaintenanceRequest request)
    {
        if (state.MaintenanceAuthority?.PrincipalId != principal.Id
            || state.Definition.AnnMaintenance != request.MaintenanceProfile)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
        }
    }
}
