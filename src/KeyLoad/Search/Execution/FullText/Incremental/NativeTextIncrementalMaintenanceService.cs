using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextIncrementalMaintenanceService : INativeTextMaintenance, ISelectedTextProjection, INativeTextSharedReadAdmission, IAsyncDisposable
{
    private const long ReleasedSequence = 0;
    private const int ReleasedRecords = 0;
    private readonly DatabaseEngine database;
    private readonly NativeTextResourceOwnership? resources;
    private readonly NativeTextIncrementalGenerationReservations generations = new();
    private readonly ServerRuntimeOptions configured;
    private readonly TimeProvider clock;
    private readonly string root;
    private readonly Action<NativeTextFaultStage>? faultObserver;
    private readonly NativeTextIncrementalWorker worker = new();
    private readonly NativeTextIncrementalSessions sessions;
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private readonly CancellationTokenSource shutdown = new();
    private readonly NativeTextSelectedReadAdmission selectedAdmission;
    private readonly Lock selectedGate = new();
    private readonly Dictionary<string, NativeTextSelectedIndexSlot> selectedSlots = [];
    private readonly Dictionary<TextIndexSelectionV1, NativeTextSelectedIndexSlot> selectedWork = [];

    internal NativeTextIncrementalMaintenanceService(DatabaseEngine database, string directory,
        Guid nodeId, ServerRuntimeOptions configured, TimeProvider clock,
        Action<NativeTextFaultStage>? faultObserver = null, NativeTextResourceOwnership? resources = null)
    {
        this.database = database;
        this.resources = resources;
        this.configured = configured;
        this.clock = clock;
        this.faultObserver = faultObserver;
        sessions = new(configured.NativeText.Value.MaximumActiveLeases);
        selectedAdmission = new(configured.NativeText.Value.MaximumActiveLeases);
        try
        {
            root = NativeTextIncrementalRoot.Initialize(Path.Combine(directory, NativeTextIncrementalProtocol.RootDirectory),
                nodeId, configured.NativeText, resources);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(shutdown.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public Task<TextMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, CancellationToken cancellationToken)
        => Task.FromResult(worker.Run(() => Execute(principal, request, null, null, cancellationToken)));

    public Task<TextMaintenanceCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, DateTimeOffset originalExpiry,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Task.FromResult(worker.Run(() => Execute(principal, request, originalExpiry, binding, token)));
    }

    private TextMaintenanceCapabilityResult Execute(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, DateTimeOffset? originalExpiry, IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        var failures = new List<Exception>();
        TextMaintenanceCapabilityResult? result = null;
        ServerFailureObserver.Observe(() =>
        {
            using var exclusive = new NativeTextSelectedWriteLease(selectedAdmission, token);
            ServerFailureObserver.Observe(() => result = ExecuteExclusive(principal, request, originalExpiry, binding, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private TextMaintenanceCapabilityResult ExecuteExclusive(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, DateTimeOffset? originalExpiry, IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        RequireSelectedSettled();
        token.ThrowIfCancellationRequested();
        NativeTextIncrementalAdmission.Request(request, database.Store.Identity.NodeId);
        var limits = configured.Core.DatabaseLimits.Value;
        if (request.Kind == TextMaintenanceCapabilityKind.Release)
        {
            using var release = new NativeMaintenanceSessionWork(configured.Core.DatabaseLimits, clock,
                originalExpiry ?? clock.GetUtcNow().AddSeconds(limits.QueryDeadlineSeconds), shutdown.Token);
            using var stage = release.EnterStage(binding, token);
            var budget = release.Budget;
            NativeTextIncrementalRelease.Execute(database, principal.Id, request.Maintenance,
                root, sessions, budget, configured.NativeText, resources, generations.ReleaseGeneration);
            var released = new TextMaintenanceCapabilityResult(request.SessionId, null,
                ReleasedSequence, ReleasedSequence, ReleasedRecords, null, null, ReleasedSequence);
            budget.CheckResult(released);
            return released;
        }
        if (request.Kind == TextMaintenanceCapabilityKind.Begin)
        {
            return NativeTextIncrementalBegin.Run(database, root, principal, request, configured, clock,
                shutdown, sessions, faultObserver, resources, generations.RetainGeneration, originalExpiry, binding, token);
        }
        var retained = sessions.Require(request.SessionId);
        retained.Work.RequireExpiry(originalExpiry);
        using var retainedStage = retained.Work.EnterStage(binding, token);
        return NativeTextIncrementalSessionOperations.Execute(database, retained,
            principal, request, configured.Core.QueryExecution.Value, limits.MaxScanRecords,
            limits.MaxScanRecords, configured.NativeText, token);
    }

    public async Task AbortAsync(Guid sessionId)
    {
        if (sessions.FindWork(sessionId) is { } work)
        {
            await work.CancelAndJoinStagesAsync().ConfigureAwait(false);
        }
        await worker.RunCleanupAsync(() =>
        {
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() =>
            {
                using var exclusive = new NativeTextSelectedWriteLease(selectedAdmission, CancellationToken.None);
                ServerFailureObserver.Observe(() =>
                {
                    RequireSelectedSettled();
                    sessions.Retire(sessionId);
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }).ConfigureAwait(false);
    }

    public NativeTextSelectedReadLease EnterBootstrapRead() => selectedAdmission.EnterRead();

    internal long SelectedPostingReads(TextIndexSelectionV1 selection)
    {
        lock (selectedGate)
        {
            return selectedWork.TryGetValue(selection, out var actual) ? actual.SuccessfulPostingReads : ReleasedSequence;
        }
    }

    private void RequireSelectedSettled()
    {
        lock (selectedGate)
        {
            foreach (var slot in selectedSlots.Values)
            {
                slot.RequireSettled();
            }
            selectedSlots.Clear();
            selectedWork.Clear();
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(shutdown.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => NativeTextIncrementalOwnerShutdown.RunAsync(
            sessions, selectedAdmission, worker.DisposeAsync().AsTask(), selectedGate, selectedSlots), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        shutdown.Dispose();
    }

    public ITextProjectionLease AcquireSelected(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
    {
        database.Authorization.Require(principal, request.Partition, request.Collection, Capability.Query);
        database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
        var reservation = resources?.ReserveLease(budget);
        try
        {
            var actual = new NativeTextSelectedProjectionLease(selectedAdmission,
                () => CaptureSelected(view, request, budget), budget, configured.Core.DatabaseLimits);
            return reservation is null ? actual : new NativeTextResourceProjectionLease(actual, reservation);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            // The native lease constructor joins its partial reader before a single original failure escapes.
            if (primary is not AggregateException && reservation is not null)
            {
                ServerFailureObserver.Observe(reservation.CompleteAfterJoinedCleanup, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private (INativeTextSelectedIndexLeaseOwner Slot, NativeTextIncrementalManifest Manifest) CaptureSelected(
        IKeyValueView view, SearchRequest request, ReadExecutionBudget budget)
    {
        var selected = NativeTextSelectedSourceAdmission.Capture(database, view, root, request, budget, configured.NativeText);
        NativeTextSelectedIndexSlot slot;
        lock (selectedGate)
        {
            if (!selectedSlots.TryGetValue(selected.Leaf, out slot!))
            {
                slot = new(root, selected.Leaf, database.Store.Identity.NodeId, configured.NativeText, resources);
                selectedSlots.Add(selected.Leaf, slot);
            }
            selectedWork[request.TextIndex!] = slot;
        }
        return (slot, selected.Manifest);
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            disposal ??= DisposeCoreAsync();
            return new(disposal);
        }
    }
}
