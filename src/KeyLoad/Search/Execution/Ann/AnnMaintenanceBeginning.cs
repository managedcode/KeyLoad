using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class AnnMaintenanceBeginning
{
    private const int BuildRetentionFrames = 2;

    internal static async Task<AnnMaintenanceCapabilityResult> RunAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceCapabilityRequest request, ServerRuntimeOptions configured,
        Lock originalGate, Func<AnnGenerationOwner?> existingRoot,
        Func<AnnGenerationOwner> owningRoot, Action<AnnMaintenanceSession> publishSession,
        MaintenanceSessionWork work)
    {
        AnnMaintenanceMemoryLease? memory = null;
        try
        {
            var read = work.Budget;
            long available;
            lock (originalGate)
            {
                memory = existingRoot()?.MaintenanceMemory.Reserve();
                available = memory?.Bytes ?? checked(configured.RuntimeAnn.Value.MaximumResidentBytes
                    - AnnRootLease.ReceiptReservation(configured.RuntimeAnn.Value));
            }
            var seedAvailable = request.Maintenance.Mode == AnnMaintenanceMode.Build
                ? available / BuildRetentionFrames : available;
            var seeds = AnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, seedAvailable);
            var captured = AnnSeedCollector.CapturePinned(database, principal.Id, request.Maintenance, seeds, read);
            AnnGenerationOwner actualOwner;
            lock (originalGate)
            {
                read.Check();
                actualOwner = owningRoot();
                memory ??= actualOwner.MaintenanceMemory.Reserve();
                _ = actualOwner.MaintenanceMemory.Remaining(captured.PeakBytesUpperBound);
            }
            var retainedMemory = memory ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
            var completed = await actualOwner.RunAsync(stages => Create(stages, actualOwner,
                retainedMemory, principal, request, captured, seeds, work, publishSession));
            memory = null;
            return completed;
        }
        catch (Exception primary)
        {
            try
            {
                memory?.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    private static AnnMaintenanceCapabilityResult Create(AnnStageStore stages, AnnGenerationOwner owner,
        AnnMaintenanceMemoryLease memory, PrincipalRecord principal, AnnMaintenanceCapabilityRequest request,
        AnnSeed captured, IOptions<AnnSeedOptions> seeds, MaintenanceSessionWork work,
        Action<AnnMaintenanceSession> publishSession)
    {
        stages.StartOriginal(request.Maintenance);
        var original = request.Maintenance.Mode == AnnMaintenanceMode.Restore
            ? stages.Describe(request.Maintenance, captured) : null;
        var created = new AnnMaintenanceSession(request.SessionId, request.Maintenance,
            principal.Id, captured, original, memory, work);
        if (request.Maintenance.Mode == AnnMaintenanceMode.Build)
        {
            _ = owner.MaintenanceMemory.Remaining(checked(captured.OwnedBytesUpperBound + seeds.Value.MaxPeakBytes));
            AnnSessionOperations.BeginBuild(created, seeds, work.Budget);
        }
        owner.MaintenanceMemory.Retain(memory, created.RetainedBytes);
        var result = AnnSessionOperations.Result(created, captured, original?.CheckpointIntent);
        publishSession(created);
        return result;
    }
}
