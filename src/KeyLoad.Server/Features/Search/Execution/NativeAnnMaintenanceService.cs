using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnMaintenanceService(string directory, DatabaseEngine database,
    ServerRuntimeOptions configured, TimeProvider clock) : INativeAnnMaintenance, IAnnProjection, IAsyncDisposable
{
    private const int Empty = 0;
    private const int BuildRetentionFrames = 2;
    private readonly Lock gate = new();
    private readonly NativeAnnMaintenanceAdmissions admitted = new();
    private NativeAnnGenerationOwner? owner;
    private NativeAnnMaintenanceSession? session;
    private bool beginning;
    private bool closed;
    private Task? disposal;

    public async Task<AnnMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken cancellationToken)
    {
        using var originalOperation = admitted.Enter(configured.GrainRouting.Value.MaximumRequestProducers);
        cancellationToken.ThrowIfCancellationRequested();
        if (!principal.ClusterAdministrator || principal.Revoked)
        { throw Errors.Fail(ErrorCode.PermissionDenied, NativeAnnProtocol.InvalidSource); }
        NativeAnnMaintenanceAdmission.Request(request.Maintenance, request.SessionId, database.Store.Identity.NodeId);
        if (request.Kind == AnnMaintenanceCapabilityKind.Release)
        { return await ReleaseAsync(principal, request, cancellationToken).ConfigureAwait(false); }
        if (request.Kind == AnnMaintenanceCapabilityKind.Begin)
        { return await BeginAsync(principal, request, cancellationToken).ConfigureAwait(false); }
        NativeAnnGenerationOwner currentOwner;
        NativeAnnMaintenanceSession currentSession;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            currentOwner = owner ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
            currentSession = session ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
            currentSession.Require(request.SessionId, request.Maintenance, principal.Id);
        }
        return await currentOwner.RunAsync(stages => NativeAnnMaintenanceStage.Execute(database, currentOwner,
            stages, currentSession, principal, request, configured, clock, cancellationToken)).ConfigureAwait(false);
    }

    public IAnnProjectionLease Acquire(IKeyValueView view, AnnProjectionSelection request,
        ReadExecutionBudget budget)
    {
        using var originalOperation = admitted.Enter(configured.GrainRouting.Value.MaximumRequestProducers);
        budget.Check();
        NativeAnnGenerationOwner actual;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            actual = owner ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
        }
        return NativeAnnPublicAcquisition.Acquire(database, view, actual, request, configured, budget);
    }

    private async Task<AnnMaintenanceCapabilityResult> ReleaseAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken token)
    {
        if (request.Maintenance.Mode != AnnMaintenanceMode.Release)
        { throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource); }
        NativeAnnReleaseValidation.Require(database, principal, request.Maintenance, token);
        NativeAnnGenerationOwner? actual;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            actual = owner;
        }
        if (actual is null && Directory.Exists(Path.Combine(directory, NativeAnnProtocol.RootDirectory)))
        { actual = EnsureOwner(); }
        if (actual is not null)
        {
            await actual.RunAsync(_ =>
            {
                NativeAnnReleaseValidation.Require(database, principal, request.Maintenance, token);
                actual.ReleaseGeneration(new(request.Maintenance.Consumer, request.Maintenance.IndexGeneration));
                return true;
            }).ConfigureAwait(false);
        }
        return new(request.SessionId, null, Empty, Empty, null, null, Empty, false);
    }

    private async Task<AnnMaintenanceCapabilityResult> BeginAsync(PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (beginning || session is not null)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound); }
            beginning = true;
        }
        NativeAnnMaintenanceMemoryLease? memory = null;
        try
        {
            var read = new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, token);
            long available;
            lock (gate)
            {
                memory = owner?.MaintenanceMemory.Reserve();
                available = memory?.Bytes ?? configured.NativeAnn.Value.MaximumResidentBytes;
            }
            var seedAvailable = request.Maintenance.Mode == AnnMaintenanceMode.Build
                ? available / BuildRetentionFrames : available;
            var seeds = NativeAnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, seedAvailable);
            var captured = AnnSeedCollector.CapturePinned(database, principal.Id, request.Maintenance, seeds, read);
            var actualOwner = CaptureOwner(captured, ref memory);
            var retainedMemory = memory ?? throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership);
            var completed = await actualOwner.RunAsync(stages => CreateSession(stages, actualOwner,
                retainedMemory, principal, request, captured, seeds, read)).ConfigureAwait(false);
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
        finally { lock (gate) { beginning = false; } }
    }

    private NativeAnnGenerationOwner CaptureOwner(AnnSeed captured, ref NativeAnnMaintenanceMemoryLease? memory)
    {
        lock (gate)
        {
            var actual = EnsureOwner();
            memory ??= actual.MaintenanceMemory.Reserve();
            _ = actual.MaintenanceMemory.Remaining(captured.PeakBytesUpperBound);
            return actual;
        }
    }

    private AnnMaintenanceCapabilityResult CreateSession(NativeAnnStageStore stages, NativeAnnGenerationOwner actualOwner,
        NativeAnnMaintenanceMemoryLease memory, PrincipalRecord principal, AnnMaintenanceCapabilityRequest request,
        AnnSeed captured, Microsoft.Extensions.Options.IOptions<AnnSeedOptions> seeds, ReadExecutionBudget read)
    {
        var original = request.Maintenance.Mode == AnnMaintenanceMode.Restore
            ? stages.Describe(request.Maintenance, captured) : null;
        var created = new NativeAnnMaintenanceSession(request.SessionId, request.Maintenance,
            principal.Id, captured, original, memory);
        if (request.Maintenance.Mode == AnnMaintenanceMode.Build)
        {
            _ = actualOwner.MaintenanceMemory.Remaining(checked(captured.OwnedBytesUpperBound + seeds.Value.MaxPeakBytes));
            NativeAnnSessionOperations.BeginBuild(created, seeds, read);
        }
        actualOwner.MaintenanceMemory.Retain(memory, created.RetainedBytes);
        var result = NativeAnnSessionOperations.Result(created, captured, original?.CheckpointIntent);
        lock (gate)
        { session = created; }
        return result;
    }

    private NativeAnnGenerationOwner EnsureOwner()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return owner ??= NativeAnnOwnership.Create(directory, database.Store.Identity,
                configured.NativeAnn, configured.Core.PackedAnn, configured.Core.PackedAnnStorage,
                configured.GrainRouting.Value.MaximumRequestProducers);
        }
    }

    internal NativeAnnMaintenanceWork? ObserveWork(Guid id)
    {
        lock (gate)
        {
            if (closed || session?.Id != id || session.Replay is null)
            { return null; }
            var observed = session.Observation;
            return new(id, session.Replay.WorkUnits, observed?.Kind, observed?.Budget.WrittenBytes ?? NativeAnnMaintenanceWork.NoWrittenBytes);
        }
    }

    public async Task AbortAsync(Guid id)
    {
        NativeAnnGenerationOwner? actual;
        lock (gate)
        { actual = owner; }
        if (actual is null)
        { return; }
        await actual.RunAsync(_ =>
        {
            lock (gate)
            { if (session?.Id == id) { session.Memory.Dispose(); session = null; } }
            return true;
        }).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closed = true;
            disposal ??= DisposeCoreAsync(admitted.CloseAndJoinAsync());
            return new(disposal);
        }
    }

    private async Task DisposeCoreAsync(Task originalOperations)
    {
        await originalOperations.ConfigureAwait(false);
        NativeAnnGenerationOwner? actual;
        lock (gate)
        { actual = owner; }
        try
        { if (actual is not null) { await actual.DisposeAsync().ConfigureAwait(false); } }
        finally { lock (gate) { session = null; } }
    }
}
