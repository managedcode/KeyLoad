using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Search;

internal sealed class TextIncrementalMaintenanceService : ITextMaintenance, ISelectedTextProjection, ITextSharedReadAdmission, IAsyncDisposable
{
    private const long ReleasedSequence = 0;
    private const int ReleasedRecords = 0;
    private readonly DatabaseEngine database;
    private readonly TextResourceOwnership? resources;
    private readonly TextIncrementalGenerationReservations generations = new();
    private readonly ServerRuntimeOptions configured;
    private readonly TimeProvider clock;
    private readonly string root;
    private readonly Action<TextFaultStage>? faultObserver;
    private readonly TextIncrementalWorker worker = new();
    private readonly TextIncrementalSessions sessions;
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private readonly CancellationTokenSource shutdown = new();
    private readonly TextSelectedReadAdmission selectedAdmission;
    private readonly Lock selectedGate = new();
    private readonly Dictionary<string, TextSelectedIndexSlot> selectedSlots = [];
    private readonly Dictionary<TextIndexSelection, TextSelectedIndexSlot> selectedWork = [];

    internal TextIncrementalMaintenanceService(DatabaseEngine database, string directory,
        Guid nodeId, ServerRuntimeOptions configured, TimeProvider clock,
        Action<TextFaultStage>? faultObserver = null, TextResourceOwnership? resources = null)
    {
        this.database = database;
        this.resources = resources;
        this.configured = configured;
        this.clock = clock;
        this.faultObserver = faultObserver;
        sessions = new();
        selectedAdmission = new();
        try
        {
            root = TextIncrementalRoot.Initialize(Path.Combine(directory, TextIncrementalProtocol.RootDirectory),
                nodeId, configured.RuntimeText, resources);
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
        => worker.RunAsync(() => ExecuteExclusiveAsync(principal, request, null, cancellationToken));

    public Task<TextMaintenanceCapabilityResult> ExecuteBoundAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return worker.RunAsync(() => ExecuteExclusiveAsync(principal, request, binding, token));
    }

    /// <summary>Waits for the selected readers to drain, runs the exclusive stage synchronously, then releases the write admission.</summary>
    private async Task<TextMaintenanceCapabilityResult> ExecuteExclusiveAsync(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        await selectedAdmission.WaitForWriteAsync(token);
        var failures = new List<Exception>();
        TextMaintenanceCapabilityResult? result = null;
        ServerFailureObserver.Observe(() => result = ExecuteExclusive(principal, request, binding, token), failures);
        ServerFailureObserver.Observe(selectedAdmission.ExitWrite, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private TextMaintenanceCapabilityResult ExecuteExclusive(PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        RequireSelectedSettled();
        token.ThrowIfCancellationRequested();
        TextIncrementalAdmission.Request(request, database.Store.Identity.NodeId);
        if (request.Kind == TextMaintenanceCapabilityKind.Release)
        {
            using var release = new MaintenanceSessionWork(clock, shutdown.Token);
            using var stage = release.EnterStage(binding, token);
            var budget = release.Budget;
            TextIncrementalRelease.Execute(database, principal.Id, request.Maintenance,
                root, sessions, budget, configured.RuntimeText, resources, generations.ReleaseGeneration);
            var released = new TextMaintenanceCapabilityResult(request.SessionId, null,
                ReleasedSequence, ReleasedSequence, ReleasedRecords, null, null, ReleasedSequence);
            return released;
        }
        if (request.Kind == TextMaintenanceCapabilityKind.Begin)
        {
            return TextIncrementalBegin.Run(database, root, principal, request, configured, clock,
                shutdown, sessions, faultObserver, resources, generations.RetainGeneration, binding, token);
        }
        var retained = sessions.Require(request.SessionId);
        using var retainedStage = retained.Work.EnterStage(binding, token);
        return TextIncrementalSessionOperations.Execute(database, retained,
            principal, request, configured.Core.QueryExecution.Value, configured.RuntimeText, token);
    }

    public async Task AbortAsync(Guid sessionId)
    {
        if (sessions.FindWork(sessionId) is { } work)
        {
            await work.CancelAndJoinStagesAsync();
        }
        await worker.RunCleanupAsync(async () =>
        {
            await selectedAdmission.WaitForWriteAsync(CancellationToken.None);
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() =>
            {
                RequireSelectedSettled();
                sessions.Retire(sessionId);
            }, failures);
            ServerFailureObserver.Observe(selectedAdmission.ExitWrite, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        });
    }

    public TextSelectedReadLease EnterBootstrapRead() => selectedAdmission.EnterRead();

    internal long SelectedPostingReads(TextIndexSelection selection)
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
        sessions.CloseAdmission();
        var originalReaders = selectedAdmission.CloseAndJoinReaders();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(shutdown.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(() => TextIncrementalOwnerShutdown.RunAsync(
            sessions, selectedAdmission, worker.DisposeAsync().AsTask(), originalReaders, selectedGate, selectedSlots), failures);
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
            var actual = new TextSelectedProjectionLease(selectedAdmission,
                () => CaptureSelected(view, request, budget), budget);
            return reservation is null ? actual : new TextResourceProjectionLease(actual, reservation);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            // The execution lease constructor joins its partial reader before a single original failure escapes.
            if (primary is not AggregateException && reservation is not null)
            {
                ServerFailureObserver.Observe(reservation.CompleteAfterJoinedCleanup, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private (ITextSelectedIndexLeaseOwner Slot, TextIncrementalManifest Manifest) CaptureSelected(
        IKeyValueView view, SearchRequest request, ReadExecutionBudget budget)
    {
        var selected = TextSelectedSourceAdmission.Capture(database, view, root, request, budget, configured.RuntimeText);
        TextSelectedIndexSlot slot;
        lock (selectedGate)
        {
            if (!selectedSlots.TryGetValue(selected.Leaf, out slot!))
            {
                slot = new(root, selected.Leaf, database.Store.Identity.NodeId, configured.RuntimeText, resources);
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
