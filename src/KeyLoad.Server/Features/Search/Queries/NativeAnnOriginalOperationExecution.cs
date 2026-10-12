using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnOriginalOperationExecution
{
    private const int CaptureFrames = 2;
    private const long EmptyBytes = 0;

    internal static async Task<(AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent)> ExecuteAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceRequest request, ProjectionConsumerInfo? originalConfigure,
        AnnMaintenanceResult? originalResult, ServerRuntimeOptions configured, TimeProvider clock,
        NativeAnnMaintenanceAdmissions admission, Func<NativeAnnGenerationOwner> owningRoot, CancellationToken token,
        NativeAnnMaintenanceSession? originalSession = null)
    {
        var failures = new List<Exception>();
        (AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent) result = default;
        try
        {
            using var operation = admission.Enter(configured.GrainRouting.Value.MaximumRequestProducers);
            try
            {
                NativeAnnMaintenanceAdmission.Request(request, request.CommandId, database.Store.Identity.NodeId);
                var owner = owningRoot();
                result = await owner.RunAsync(stages => Run(database, principal, request, originalConfigure,
                    originalResult, configured, clock, owner, stages, originalSession, token)).ConfigureAwait(false);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static (AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent) Run(DatabaseEngine database, PrincipalRecord principal,
        AnnMaintenanceRequest request, ProjectionConsumerInfo? originalConfigure, AnnMaintenanceResult? originalResult,
        ServerRuntimeOptions configured, TimeProvider clock, NativeAnnGenerationOwner owner,
        NativeAnnStageStore stages, NativeAnnMaintenanceSession? originalSession, CancellationToken token)
    {
        var failures = new List<Exception>();
        AnnMaintenanceResult? result = null;
        CommitProjectionBatchRequest? settlement = null;
        try
        {
            using var memory = originalSession is null ? owner.MaintenanceMemory.Reserve() : null;
            try
            {
                var read = new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, token);
                var fresh = database.Store.Read(view => database.Principal(view, principal.Id, clock.GetUtcNow()));
                if (!fresh.ClusterAdministrator || fresh.Revoked || fresh.PolicyEpoch != principal.PolicyEpoch)
                { throw Errors.Fail(ErrorCode.PermissionDenied, NativeAnnProtocol.InvalidSource); }
                if (originalSession is not null) { owner.MaintenanceMemory.Expand(originalSession.Memory); }
                var retained = originalSession?.RetainedBytes ?? EmptyBytes;
                var available = owner.MaintenanceMemory.Remaining(retained);
                var seeds = NativeAnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, available / CaptureFrames);
                var current = AnnSeedCollector.CapturePinned(database, fresh.Id, request, seeds, read);
                read.Check();
                var final = database.Store.Read(view => database.Principal(view, principal.Id, clock.GetUtcNow()));
                if (!final.ClusterAdministrator || final.Revoked || final.PolicyEpoch != fresh.PolicyEpoch
                    || current.Scope.PolicyEpoch != fresh.PolicyEpoch
                    || originalSession is not null && originalSession.PrincipalId != fresh.Id)
                { throw Errors.Fail(ErrorCode.PermissionDenied, NativeAnnProtocol.InvalidSource); }
                (result, settlement) = ApplyOriginal(database, fresh, request, originalConfigure, originalResult,
                    stages, owner, current, seeds.Value, retained, read);
                if (result is not null) { read.ChargeBytes(global::KeyLoad.NativeSerialization.Measure(result)); }
                if (settlement is not null) { read.ChargeBytes(global::KeyLoad.NativeSerialization.Measure(settlement)); }
                read.Check();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return (result, settlement);
    }
    private static (AnnMaintenanceResult? Result, CommitProjectionBatchRequest? Intent) ApplyOriginal(
        DatabaseEngine database, PrincipalRecord fresh, AnnMaintenanceRequest request,
        ProjectionConsumerInfo? originalConfigure, AnnMaintenanceResult? originalResult,
        NativeAnnStageStore stages, NativeAnnGenerationOwner owner, AnnSeed current,
        AnnSeedOptions seeds, long retained, ReadExecutionBudget read)
    {
        AnnMaintenanceResult? result;
        CommitProjectionBatchRequest? settlement = null;
        var configuredCurrent = database.ReadAnnProjectionConsumer(fresh, request);
        read.ChargeBytes(global::KeyLoad.NativeSerialization.Measure(configuredCurrent));
        if (originalConfigure is not null && (originalConfigure.Consumer != configuredCurrent.Consumer
            || !global::KeyLoad.NativeSerialization.Serialize(originalConfigure.Definition).AsSpan()
                .SequenceEqual(global::KeyLoad.NativeSerialization.Serialize(configuredCurrent.Definition))
            || originalConfigure.MaintenanceAuthority != configuredCurrent.MaintenanceAuthority))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource); }
        if (originalResult is null)
        {
            var admittedConfigure = originalConfigure is null ? configuredCurrent
                : configuredCurrent with { Checkpoint = originalConfigure.Checkpoint };
            result = stages.AdmitOriginal(fresh, request, admittedConfigure, current);
            if (result is not null)
            {
                RequirePublished(stages, owner, request, result, current, seeds, retained, read);
                settlement = stages.OriginalSettlementIntent(request);
                if (configuredCurrent.InitialAnnMaintenance is { } initial
                    && NativeAnnOriginalResults.SameRequest(initial, request) && settlement is null)
                { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
            }
        }
        else
        {
            RequirePublished(stages, owner, request, originalResult, current, seeds, retained, read);
            read.ChargeBytes(global::KeyLoad.NativeSerialization.Measure(request));
            read.ChargeBytes(global::KeyLoad.NativeSerialization.Measure(originalResult));
            settlement = stages.OriginalSettlementIntent(request)
                ?? database.CreateAnnInitialSettlementIntent(fresh, request, originalResult,
                    AnnMaintenanceChildIdentity.Create(request.CommandId, AnnMaintenancePhase.Completed,
                        originalResult.Checkpoint?.Checkpoint ?? originalResult.Source?.ThroughSequence
                            ?? throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt)));
            stages.SettleOriginal(fresh, request, originalResult, current, settlement);
            result = originalResult;
        }
        return (result, settlement);
    }

    private static void RequirePublished(NativeAnnStageStore stages, NativeAnnGenerationOwner owner,
        AnnMaintenanceRequest request, AnnMaintenanceResult result, AnnSeed current, AnnSeedOptions seeds,
        long retained, ReadExecutionBudget read)
    {
        var work = new AnnWorkBudget(read, seeds.MaxWorkUnits);
        var publication = stages.ReadPublished(request, current, seeds,
            owner.MaintenanceMemory.Remaining(checked(retained + current.OwnedBytesUpperBound)), work, read);
        if (publication.Count != result.Count || Convert.ToHexStringLower(publication.IndexSha256) != result.IndexSha256)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        NativeAnnOriginalResults.RequirePublication(result, publication, current);
    }

}
