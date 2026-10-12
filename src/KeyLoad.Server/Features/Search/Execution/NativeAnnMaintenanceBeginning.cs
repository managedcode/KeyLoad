using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnMaintenanceBeginning
{
    private const int BuildRetentionFrames = 2;

    internal static async Task<AnnMaintenanceCapabilityResult> RunAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceCapabilityRequest request, ServerRuntimeOptions configured,
        TimeProvider clock, Lock originalGate, Func<NativeAnnGenerationOwner?> existingRoot,
        Func<NativeAnnGenerationOwner> owningRoot, Action<NativeAnnMaintenanceSession> publishSession,
        CancellationToken token)
    {
        NativeAnnMaintenanceMemoryLease? memory = null;
        try
        {
            var read = new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, token);
            long available;
            lock (originalGate)
            {
                memory = existingRoot()?.MaintenanceMemory.Reserve();
                available = memory?.Bytes ?? checked(configured.NativeAnn.Value.MaximumResidentBytes
                    - NativeAnnRootLease.ReceiptReservation(configured.NativeAnn.Value));
            }
            var seedAvailable = request.Maintenance.Mode == AnnMaintenanceMode.Build
                ? available / BuildRetentionFrames : available;
            var seeds = NativeAnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, seedAvailable);
            var captured = AnnSeedCollector.CapturePinned(database, principal.Id, request.Maintenance, seeds, read);
            NativeAnnGenerationOwner actualOwner;
            lock (originalGate)
            {
                actualOwner = owningRoot();
                memory ??= actualOwner.MaintenanceMemory.Reserve();
                _ = actualOwner.MaintenanceMemory.Remaining(captured.PeakBytesUpperBound);
            }
            var retainedMemory = memory ?? throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership);
            var completed = await actualOwner.RunAsync(stages => Create(stages, actualOwner,
                retainedMemory, principal, request, captured, seeds, read, publishSession)).ConfigureAwait(false);
            memory = null;
            return completed;
        }
        catch (Exception primary)
        {
            try
            { memory?.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    private static AnnMaintenanceCapabilityResult Create(NativeAnnStageStore stages, NativeAnnGenerationOwner owner,
        NativeAnnMaintenanceMemoryLease memory, PrincipalRecord principal, AnnMaintenanceCapabilityRequest request,
        AnnSeed captured, IOptions<AnnSeedOptions> seeds, ReadExecutionBudget read,
        Action<NativeAnnMaintenanceSession> publishSession)
    {
        stages.StartOriginal(request.Maintenance);
        var original = request.Maintenance.Mode == AnnMaintenanceMode.Restore
            ? stages.Describe(request.Maintenance, captured) : null;
        var created = new NativeAnnMaintenanceSession(request.SessionId, request.Maintenance,
            principal.Id, captured, original, memory);
        if (request.Maintenance.Mode == AnnMaintenanceMode.Build)
        {
            _ = owner.MaintenanceMemory.Remaining(checked(captured.OwnedBytesUpperBound + seeds.Value.MaxPeakBytes));
            NativeAnnSessionOperations.BeginBuild(created, seeds, read);
        }
        owner.MaintenanceMemory.Retain(memory, created.RetainedBytes);
        var result = NativeAnnSessionOperations.Result(created, captured, original?.CheckpointIntent);
        publishSession(created);
        return result;
    }
}
