using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Orleans.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineMaintenanceService : INativeOnlineTextMaintenance, IAsyncDisposable
{
    private readonly DatabaseEngine database;
    private readonly ServerRuntimeOptions options;
    private readonly TimeProvider clock;
    private readonly NativeTextResourceOwnership resources;
    private readonly NativeTextOnlineCatalogOwner catalog;
    private readonly NativeTextOnlineOriginalReplay replay;
    private readonly NativeTextOnlineRoot root;
    private readonly NativeTextOnlineSessions sessions;
    private readonly NativeTextOnlineWorker worker = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly Lock disposalGate = new();
    private Task? disposal;

    internal NativeTextOnlineMaintenanceService(DatabaseEngine database, string directory, Guid nodeId,
        ServerRuntimeOptions options, TimeProvider clock, NativeTextResourceOwnership resources,
        Action<NativeTextFaultStage>? observer = null)
    {
        this.database = database;
        this.options = options;
        this.clock = clock;
        this.resources = resources;
        sessions = new(options.NativeText.Value.MaximumActiveLeases);
        try
        { root = new(directory, nodeId, options.NativeText, resources); }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(shutdown.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        catalog = new(database, root, options, clock, resources, observer);
        replay = new(database, options, clock, resources, sessions, catalog, CleanupSessionAsync);
    }

    public Task<OnlineTextCapabilityResult> ExecuteAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset originalExpiry, CancellationToken token)
        => worker.RunAsync(() => ExecuteOwnedAsync(principal, request, originalExpiry, token));

    private async Task<OnlineTextCapabilityResult> ExecuteOwnedAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset expiry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await catalog.ObserveCompletedRetirementsAsync().ConfigureAwait(false);
        if (!principal.ClusterAdministrator || request.SessionId == Guid.Empty
            || request.Request.NodeId != database.Store.Identity.NodeId)
        { throw NativeTextErrors.Ownership(); }
        if (request.Kind == OnlineTextCapabilityKind.ResolveOriginal)
        {
            var resolved = await ResolveCommittedOriginalAsync(principal, request, expiry, token).ConfigureAwait(false);
            if (resolved is not null)
            { return resolved; }
        }
        var session = RequireSession(principal, request, expiry);
        using var stage = session.Budget.EnterStageCancellation(token);
        session.Require(request.SessionId, request.Request, principal.Id, expiry);
        if (request.Kind == OnlineTextCapabilityKind.SettleCheckpoint)
        {
            await NativeTextOnlineCheckpointSettlement.ExecuteAsync(database, session,
                request.Acknowledged ?? throw NativeTextErrors.Corrupt(), resources, options).ConfigureAwait(false);
        }
        else if (request.Kind == OnlineTextCapabilityKind.ReconcileCommitted)
        { await catalog.ReconcileCommittedAsync(session).ConfigureAwait(false); }
        else
        {
            lock (session.Gate)
            { return NativeTextOnlineStageExecutor.Execute(database, root, resources, options, clock, RequireCurrentAuthority, principal, session, request); }
        }
        return NativeTextOnlineCapabilityResult.Create(session);
    }

    private NativeTextOnlineSession RequireSession(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset expiry)
    {
        if (request.Kind != OnlineTextCapabilityKind.ResolveOriginal)
        { return sessions.Require(request.SessionId); }
        var session = new NativeTextOnlineSession(request.SessionId, request.Request, principal.Id,
            expiry, options, clock, shutdown.Token);
        try
        { sessions.Add(session); }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => session.CloseCommandAdmissionAndJoinAsync().GetAwaiter().GetResult(), failures);
            ServerFailureObserver.Observe(session.DisposeAfterJoinedNativeOwners, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        return session;
    }

    private void RequireCurrentAuthority(NativeTextOnlineSession session)
    {
        var fresh = NativeTextOnlineCapture.CurrentMetadata(database, session);
        NativeTextOnlineCapture.RequireSameAuthority(session, fresh);
        session.Current = fresh;
    }

    public OnlineTextPublicationWork RequirePublicationWork(Guid sessionId,
        OnlineTextIndexMaintenanceRequest request, string principalId)
    {
        var session = sessions.Require(sessionId);
        lock (session.Gate)
        {
            session.Require(sessionId, request, principalId, session.OriginalExpiry);
            return session.RequirePreparedPublication();
        }
    }

    public OnlineTextPublicationWork? TryRequireCheckpointWork(CommitProjectionBatchRequest request,
        string principalId, DateTimeOffset originalExpiry)
        => sessions.TryMatchCheckpoint(request, principalId, originalExpiry);

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        { disposal ??= DisposeCoreAsync(); return new(disposal); }
    }

    internal Task[] CaptureOriginalRetirements() => catalog.CaptureOriginalRetirements();

    internal NativeTextSelectedProjectionLease? AcquireCurrent(KeyLoad.Storage.IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
        => catalog.AcquireCurrent(view, principal, resource, request, budget);
}
