using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextIncrementalMaintenanceService : INativeTextMaintenance, ISelectedTextProjection, INativeTextSharedReadAdmission, IAsyncDisposable
{
    private const int EmptyFailures = 0;
    private const long ReleasedSequence = 0;
    private const int ReleasedRecords = 0;
    private readonly DatabaseEngine database;
    private readonly NativeTextResourceOwnership? resources;
    private readonly Dictionary<string, NativeTextResourceReservation> generationReservations = new(StringComparer.Ordinal);
    private readonly ServerRuntimeOptions configured;
    private readonly TimeProvider clock;
    private readonly string root;
    private readonly Action<NativeTextFaultStage>? faultObserver;
    private readonly NativeTextIncrementalWorker worker = new();
    private readonly NativeTextIncrementalSessions sessions;
    private readonly Lock disposalGate = new();
    private Task? disposal;
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
        root = NativeTextIncrementalRoot.Initialize(Path.Combine(directory, NativeTextIncrementalProtocol.RootDirectory),
            nodeId, configured.NativeText, resources);
    }

    public Task<TextMaintenanceCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, CancellationToken cancellationToken)
        => worker.RunAsync(() => Execute(principal, request, cancellationToken));

    private TextMaintenanceCapabilityResult Execute(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, CancellationToken token)
    {
        var failures = new List<Exception>();
        TextMaintenanceCapabilityResult? result = null;
        ServerFailureObserver.Observe(() =>
        {
            using var exclusive = new NativeTextSelectedWriteLease(selectedAdmission, token);
            ServerFailureObserver.Observe(() => result = ExecuteExclusive(principal, request, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private TextMaintenanceCapabilityResult ExecuteExclusive(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, CancellationToken token)
    {
        RequireSelectedSettled();
        token.ThrowIfCancellationRequested();
        NativeTextIncrementalAdmission.Request(request, database.Store.Identity.NodeId);
        var limits = configured.Core.DatabaseLimits.Value;
        if (request.Kind == TextMaintenanceCapabilityKind.Release)
        {
            var budget = new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, token);
            NativeTextIncrementalRelease.Execute(database, principal.Id, request.Maintenance,
                root, sessions, budget, configured.NativeText, resources, ReleaseGeneration);
            var released = new TextMaintenanceCapabilityResult(request.SessionId, null,
                ReleasedSequence, ReleasedSequence, ReleasedRecords, null, null, ReleasedSequence);
            budget.CheckResult(released);
            return released;
        }
        if (request.Kind == TextMaintenanceCapabilityKind.Begin)
        {
            var budget = new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, token);
            var actual = NativeTextIncrementalBegin.Capture(database, root, request.SessionId, principal, request.Maintenance,
                budget, limits.MaxScanRecords, limits.MaxScanRecords, configured.NativeText, faultObserver, resources, RetainGeneration);
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() => sessions.Add(actual), failures);
            if (failures.Count != EmptyFailures)
            {
                ServerFailureObserver.Observe(() => NativeTextIncrementalSessionLifetime.DisposeNative(actual), failures);
                ServerFailureObserver.ThrowIfAny(failures);
            }
            actual.Budget.Check();
            return NativeTextIncrementalCapabilityResult.Create(actual, actual.Upper);
        }
        return NativeTextIncrementalSessionOperations.Execute(database, sessions.Require(request.SessionId),
            principal, request, configured.Core.QueryExecution.Value, limits.MaxScanRecords,
            limits.MaxScanRecords, configured.NativeText, token);
    }

    public async Task AbortAsync(Guid sessionId)
    {
        _ = await worker.RunAsync(() =>
        {
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() =>
            {
                using var exclusive = new NativeTextSelectedWriteLease(selectedAdmission, CancellationToken.None);
                ServerFailureObserver.Observe(() => { RequireSelectedSettled(); sessions.Retire(sessionId); }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            return sessionId;
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
            { slot.RequireSettled(); }
            selectedSlots.Clear();
            selectedWork.Clear();
        }
    }

    private async Task DisposeCoreAsync()
    {
        sessions.CloseAdmission();
        var originalReaders = selectedAdmission.CloseAndJoinReaders();
        var failures = new List<Exception>();
        try
        { await worker.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        await ServerFailureObserver.ObserveAsync(() => originalReaders, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(selectedAdmission.ThrowRetained, failures);
        NativeTextSelectedIndexSlot[] originals;
        lock (selectedGate)
        { originals = selectedSlots.Values.ToArray(); }
        foreach (var original in originals)
        { ServerFailureObserver.Observe(original.Dispose, failures); }
        ServerFailureObserver.Observe(sessions.DisposeAfterWorkerJoin, failures);
        ServerFailureObserver.ThrowIfAny(failures);
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
            { ServerFailureObserver.Observe(reservation.CompleteAfterJoinedCleanup, failures); }
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

    private void RetainGeneration(string leaf, NativeTextResourceReservation original)
    {
        // The worker owns this registry; every entry corresponds to prospective or actual retained files.
        if (!generationReservations.TryAdd(leaf, original))
        { throw NativeTextErrors.Ownership(); }
    }

    private void ReleaseGeneration(string leaf)
    {
        if (generationReservations.TryGetValue(leaf, out var original))
        {
            original.CompleteAfterJoinedCleanup();
            generationReservations.Remove(leaf);
        }
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
