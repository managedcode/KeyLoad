using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal static class AnnOriginalOperationExecution
{
    private const int CaptureFrames = 2;
    private const long EmptyBytes = 0;

    internal static async Task<(AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent)> ExecuteAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceRequest request, ProjectionConsumerInfo? originalConfigure,
        AnnMaintenanceResult? originalResult, ServerRuntimeOptions configured, TimeProvider clock,
        AnnMaintenanceAdmissions admission, Func<AnnGenerationOwner> owningRoot, CancellationToken token,
        AnnMaintenanceSession? originalSession = null, ReadExecutionBudget? originalBudget = null)
    {
        var failures = new List<Exception>();
        (AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent) result = default;
        try
        {
            using var operation = admission.Enter();
            try
            {
                AnnMaintenanceAdmission.Request(request, request.CommandId, database.Store.Identity.NodeId);
                var owner = owningRoot();
                result = await owner.RunAsync(stages => Run(database, principal, request, originalConfigure,
                    originalResult, configured, clock, owner, stages, originalSession, originalBudget, token));
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static (AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent) Run(DatabaseEngine database, PrincipalRecord principal,
        AnnMaintenanceRequest request, ProjectionConsumerInfo? originalConfigure, AnnMaintenanceResult? originalResult,
        ServerRuntimeOptions configured, TimeProvider clock, AnnGenerationOwner owner,
        AnnStageStore stages, AnnMaintenanceSession? originalSession, ReadExecutionBudget? originalBudget, CancellationToken token)
    {
        var failures = new List<Exception>();
        AnnMaintenanceResult? result = null;
        CommitProjectionBatchRequest? settlement = null;
        try
        {
            using var memory = originalSession is null ? owner.MaintenanceMemory.Reserve() : null;
            try
            {
                var read = originalBudget ?? new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, token);
                var fresh = database.Store.Read(view => database.Principal(view, principal.Id, clock.GetUtcNow()));
                if (!fresh.ClusterAdministrator || fresh.Revoked || fresh.PolicyEpoch != principal.PolicyEpoch)
                {
                    throw Errors.Fail(ErrorCode.PermissionDenied, AnnProtocol.InvalidSource);
                }
                if (originalSession is not null)
                {
                    owner.MaintenanceMemory.Expand(originalSession.Memory);
                }
                var retained = originalSession?.RetainedBytes ?? EmptyBytes;
                var available = owner.MaintenanceMemory.Remaining(retained);
                var seeds = AnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, available / CaptureFrames);
                var current = AnnSeedCollector.CapturePinned(database, fresh.Id, request, seeds, read);
                read.Check();
                var final = database.Store.Read(view => database.Principal(view, principal.Id, clock.GetUtcNow()));
                if (!final.ClusterAdministrator || final.Revoked || final.PolicyEpoch != fresh.PolicyEpoch
                    || current.Scope.PolicyEpoch != fresh.PolicyEpoch
                    || originalSession is not null && originalSession.PrincipalId != fresh.Id)
                {
                    throw Errors.Fail(ErrorCode.PermissionDenied, AnnProtocol.InvalidSource);
                }
                (result, settlement) = ApplyOriginal(database, fresh, request, originalConfigure, originalResult,
                    stages, owner, current, seeds.Value, retained, read);
                if (result is not null)
                {
                    read.ChargeBytes(global::KeyLoad.OrleansSerialization.Measure(result));
                }
                if (settlement is not null)
                {
                    read.ChargeBytes(global::KeyLoad.OrleansSerialization.Measure(settlement));
                }
                read.Check();
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return (result, settlement);
    }
    private static (AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent) ApplyOriginal(
        DatabaseEngine database, PrincipalRecord fresh, AnnMaintenanceRequest request,
        ProjectionConsumerInfo? originalConfigure, AnnMaintenanceResult? originalResult,
        AnnStageStore stages, AnnGenerationOwner owner, AnnSeed current,
        AnnSeedOptions seeds, long retained, ReadExecutionBudget read)
    {
        AnnMaintenanceResult? result;
        CommitProjectionBatchRequest? settlement = null;
        var configuredCurrent = database.ReadAnnProjectionConsumer(fresh, request);
        read.ChargeBytes(global::KeyLoad.OrleansSerialization.Measure(configuredCurrent));
        if (originalConfigure is not null && (originalConfigure.Consumer != configuredCurrent.Consumer
            || !global::KeyLoad.OrleansSerialization.Serialize(originalConfigure.Definition).AsSpan()
                .SequenceEqual(global::KeyLoad.OrleansSerialization.Serialize(configuredCurrent.Definition))
            || originalConfigure.MaintenanceAuthority != configuredCurrent.MaintenanceAuthority))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
        }
        if (originalResult is null)
        {
            var admittedConfigure = originalConfigure is null ? configuredCurrent
                : configuredCurrent with
                {
                    Checkpoint = originalConfigure.Checkpoint
                };
            result = stages.AdmitOriginal(fresh, request, admittedConfigure, current);
            if (result is not null)
            {
                RequirePublished(stages, owner, request, result, current, seeds, retained, read);
                settlement = stages.OriginalSettlementIntent(request);
                if (configuredCurrent.InitialAnnMaintenance is { } initial
                    && AnnOriginalResults.SameRequest(initial, request) && settlement is null)
                {
                    throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
                }
            }
        }
        else
        {
            RequirePublished(stages, owner, request, originalResult, current, seeds, retained, read);
            read.ChargeBytes(global::KeyLoad.OrleansSerialization.Measure(request));
            read.ChargeBytes(global::KeyLoad.OrleansSerialization.Measure(originalResult));
            settlement = stages.OriginalSettlementIntent(request)
                ?? database.CreateAnnInitialSettlementIntent(fresh, request, originalResult,
                    AnnMaintenanceChildIdentity.Create(request.CommandId, AnnMaintenancePhase.Completed,
                        originalResult.Checkpoint?.Checkpoint ?? originalResult.Source?.ThroughSequence
                            ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt)));
            stages.SettleOriginal(fresh, request, originalResult, current, settlement);
            result = originalResult;
        }
        return (result, settlement);
    }

    private static void RequirePublished(AnnStageStore stages, AnnGenerationOwner owner,
        AnnMaintenanceRequest request, AnnMaintenanceResult result, AnnSeed current, AnnSeedOptions seeds,
        long retained, ReadExecutionBudget read)
    {
        var work = new AnnWorkBudget(read, seeds.MaxWorkUnits);
        var publication = stages.ReadPublished(request, current, seeds,
            owner.MaintenanceMemory.Remaining(checked(retained + current.OwnedBytesUpperBound)), work, read);
        if (publication.Count != result.Count || Convert.ToHexStringLower(publication.IndexSha256) != result.IndexSha256)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
        AnnOriginalResults.RequirePublication(result, publication, current);
    }

}
